using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Playserv.RPC;

namespace Playserv.Wrapper
{
    internal sealed class PlayServApiRpcFacade
    {
        private readonly IPlayServRpcRuntimeAccess _runtimeAccess;
        private readonly RpcPendingRequestRegistry _pendingRequests = new RpcPendingRequestRegistry();

        public event Action<InvokeRpcResponse> OnRpcInvokeResponse;

        public PlayServApiRpcFacade()
            : this(new PlayServRpcRuntimeAccess())
        {
        }

        public PlayServApiRpcFacade(IPlayServRpcRuntimeAccess runtimeAccess)
        {
            _runtimeAccess = runtimeAccess ?? throw new ArgumentNullException(nameof(runtimeAccess));
            _runtimeAccess.ModuleCommandReceived += HandleModuleCommand;
        }

        public void Send<T>(T command) => _runtimeAccess.Send(command);

        public void Send<T>(T command, string moduleName) => _runtimeAccess.Send(command, moduleName);

        public void Invoke(string serviceName, string methodName, object payload)
        {
            var payloadBase64 = RpcPayloadSerializer.SerializeToBase64(payload, _runtimeAccess.ResolveJsonCodec());
            Invoke(serviceName, methodName, payloadBase64);
        }

        public Task<PlayServRpcResult<TResponse>> InvokeAsync<TRequest, TResponse>(
            string serviceName,
            string methodName,
            TRequest request,
            PlayServRpcInvokeOptions options,
            CancellationToken cancellationToken)
        {
            ValidateInvocation(serviceName, methodName);

            var timeout = options?.Timeout ?? PlayServRpcInvokeOptions.DefaultTimeout;
            if (timeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(options), "RPC timeout must be greater than zero.");
            if (timeout.TotalMilliseconds > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    $"RPC timeout cannot exceed {int.MaxValue} milliseconds.");
            }

            var requestId = ResolveRequestId(options?.RequestId);
            var coalesceKey = options?.CoalesceKey;
            return InvokeAsyncCore<TRequest, TResponse>(
                requestId,
                serviceName,
                methodName,
                request,
                coalesceKey,
                timeout,
                cancellationToken);
        }

        public void InvokeArgs(string serviceName, string methodName, params object[] args) =>
            InvokeMapped(serviceName, methodName, RpcMappedPayload.Positional(args ?? Array.Empty<object>()));

        public void InvokeNamed(string serviceName, string methodName, IDictionary<string, object> payload)
        {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));

            InvokeMapped(serviceName, methodName, RpcMappedPayload.Named(payload));
        }

        public void Invoke(string serviceName, string methodName, string payloadBase64)
            => InvokeCore(serviceName, methodName, payloadBase64, coalesceKey: null);

        public void Invoke(string serviceName, string methodName, object payload, string coalesceKey)
        {
            var payloadBase64 = RpcPayloadSerializer.SerializeToBase64(payload, _runtimeAccess.ResolveJsonCodec());
            InvokeCore(serviceName, methodName, payloadBase64, coalesceKey);
        }

        public void Invoke(string serviceName, string methodName, object payload, string coalesceKey, bool fireAndForget)
        {
            var payloadBase64 = RpcPayloadSerializer.SerializeToBase64(payload, _runtimeAccess.ResolveJsonCodec());
            InvokeCore(serviceName, methodName, payloadBase64, coalesceKey, fireAndForget);
        }

        private void InvokeCore(string serviceName, string methodName, string payloadBase64, string coalesceKey, bool fireAndForget = false)
        {
            ValidateInvocation(serviceName, methodName);

            if (string.IsNullOrWhiteSpace(payloadBase64))
                throw new ArgumentException("Payload base64 is required.", nameof(payloadBase64));

            if (_runtimeAccess.LocalExecution.TryInvokeRpc(serviceName, methodName, payloadBase64, _runtimeAccess.HasCurrentInstance))
                return;

            var request = new InvokeRpc
            {
                ServiceName = serviceName,
                MethodName = methodName,
                Payload = payloadBase64,
                CoalesceKey = coalesceKey,
                FireAndForget = fireAndForget
            };

            _runtimeAccess.Send(request, RpcConstants.InvokeModuleServiceName);
        }

        private async Task<PlayServRpcResult<TResponse>> InvokeAsyncCore<TRequest, TResponse>(
            string requestId,
            string serviceName,
            string methodName,
            TRequest requestPayload,
            string coalesceKey,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Failure<TResponse>(
                    requestId,
                    PlayServRpcErrorCode.Canceled,
                    "RPC invocation was canceled before it was sent.");
            }

            string payloadBase64;
            Playserv.Serialization.IJsonCodec jsonCodec;
            try
            {
                jsonCodec = _runtimeAccess.ResolveJsonCodec();
                payloadBase64 = RpcPayloadSerializer.SerializeToBase64(requestPayload, jsonCodec);
            }
            catch (Exception ex)
            {
                return Failure<TResponse>(
                    requestId,
                    PlayServRpcErrorCode.SerializationFailed,
                    "Failed to serialize the RPC request payload.",
                    exception: ex);
            }

            PendingRpcRequest pending;
            try
            {
                pending = _pendingRequests.Register(requestId, serviceName, methodName);
            }
            catch (Exception ex)
            {
                return Failure<TResponse>(
                    requestId,
                    PlayServRpcErrorCode.InvalidResponse,
                    ex.Message,
                    exception: ex);
            }

            try
            {
                _runtimeAccess.Send(
                    new InvokeRpc
                    {
                        RequestId = requestId,
                        ServiceName = serviceName,
                        MethodName = methodName,
                        Payload = payloadBase64,
                        CoalesceKey = coalesceKey,
                        FireAndForget = false
                    },
                    RpcConstants.InvokeModuleServiceName);
            }
            catch (Exception ex)
            {
                _pendingRequests.TryRemove(pending);
                return Failure<TResponse>(
                    requestId,
                    PlayServRpcErrorCode.TransportFailed,
                    "Failed to send the RPC request.",
                    exception: ex);
            }

            var responseTask = pending.Completion.Task;
            var waitTask = Task.Delay(timeout, cancellationToken);
            var completedTask = await Task.WhenAny(responseTask, waitTask);

            if (completedTask != responseTask)
            {
                if (!_pendingRequests.TryRemove(pending))
                    return ConvertResponse<TResponse>(requestId, await responseTask, jsonCodec);

                if (cancellationToken.IsCancellationRequested)
                {
                    return Failure<TResponse>(
                        requestId,
                        PlayServRpcErrorCode.Canceled,
                        "RPC invocation was canceled.");
                }

                return Failure<TResponse>(
                    requestId,
                    PlayServRpcErrorCode.Timeout,
                    $"RPC invocation timed out after {timeout.TotalSeconds:0.###} seconds.");
            }

            return ConvertResponse<TResponse>(requestId, await responseTask, jsonCodec);
        }

        private static PlayServRpcResult<TResponse> ConvertResponse<TResponse>(
            string requestId,
            InvokeRpcResponse response,
            Playserv.Serialization.IJsonCodec jsonCodec)
        {
            if (response == null)
            {
                return Failure<TResponse>(
                    requestId,
                    PlayServRpcErrorCode.InvalidResponse,
                    "RPC response was null.");
            }

            if (!IsSuccessStatus(response.Status))
            {
                return Failure<TResponse>(
                    requestId,
                    PlayServRpcErrorCode.ServerError,
                    string.IsNullOrWhiteSpace(response.Message)
                        ? "The RPC server rejected the invocation."
                        : response.Message,
                    response.Status,
                    response);
            }

            if (string.IsNullOrWhiteSpace(response.Result))
            {
                return new PlayServRpcResult<TResponse>(
                    requestId,
                    default,
                    response.Result,
                    response.Status,
                    response.Message,
                    response.Timestamp,
                    error: null);
            }

            try
            {
                var value = jsonCodec.Deserialize<TResponse>(response.Result);
                return new PlayServRpcResult<TResponse>(
                    requestId,
                    value,
                    response.Result,
                    response.Status,
                    response.Message,
                    response.Timestamp,
                    error: null);
            }
            catch (Exception ex)
            {
                return Failure<TResponse>(
                    requestId,
                    PlayServRpcErrorCode.DeserializationFailed,
                    $"Failed to deserialize the RPC response as {typeof(TResponse).Name}.",
                    response.Status,
                    response,
                    ex);
            }
        }

        private static PlayServRpcResult<T> Failure<T>(
            string requestId,
            PlayServRpcErrorCode code,
            string message,
            string status = null,
            InvokeRpcResponse response = null,
            Exception exception = null)
        {
            return new PlayServRpcResult<T>(
                requestId,
                default,
                response?.Result,
                status ?? response?.Status,
                response?.Message ?? message,
                response?.Timestamp ?? DateTimeOffset.UtcNow,
                new PlayServRpcError(code, message, status ?? response?.Status, exception));
        }

        private static bool IsSuccessStatus(string status)
        {
            return string.IsNullOrWhiteSpace(status) ||
                   string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(status, "success", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(status, "succeeded", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(status, "accepted", StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveRequestId(string requestedId)
        {
            if (requestedId == null)
                return Guid.NewGuid().ToString("N");

            if (string.IsNullOrWhiteSpace(requestedId))
                throw new ArgumentException("RPC request id cannot be empty.", nameof(requestedId));

            if (requestedId.Length > 128)
                throw new ArgumentException("RPC request id cannot exceed 128 characters.", nameof(requestedId));

            return requestedId;
        }

        private static void ValidateInvocation(string serviceName, string methodName)
        {
            if (string.IsNullOrWhiteSpace(serviceName))
                throw new ArgumentException("Service name is required.", nameof(serviceName));

            if (string.IsNullOrWhiteSpace(methodName))
                throw new ArgumentException("Method name is required.", nameof(methodName));
        }

        public void Invoke<TService>(Expression<Action<TService>> method)
        {
            if (method == null)
                throw new ArgumentNullException(nameof(method));

            var serviceName = ResolveServiceName<TService>();
            var methodCall = ResolveMethodCall(method.Body, nameof(method));
            var payload = RpcPayloadMapper.BuildPayloadFromMethodCall(methodCall);
            InvokeMapped(serviceName, methodCall.Method.Name, payload);
        }

        public void Invoke<TService>(Expression<Action<TService>> method, object payload)
        {
            if (method == null)
                throw new ArgumentNullException(nameof(method));

            var serviceName = ResolveServiceName<TService>();
            var methodCall = ResolveMethodCall(method.Body, nameof(method));
            var normalizedPayload = RpcPayloadMapper.BuildPayloadFromExplicitPayload(methodCall, payload);
            InvokeMapped(serviceName, methodCall.Method.Name, normalizedPayload);
        }

        public void Invoke<TService>(Expression<Action<TService>> method, string payloadBase64)
        {
            if (method == null)
                throw new ArgumentNullException(nameof(method));

            var serviceName = ResolveServiceName<TService>();
            var methodCall = ResolveMethodCall(method.Body, nameof(method));
            Invoke(serviceName, methodCall.Method.Name, payloadBase64);
        }

        private void InvokeMapped(string serviceName, string methodName, RpcMappedPayload payload)
        {
            var payloadBase64 = RpcPayloadSerializer.SerializeToBase64(payload, _runtimeAccess.ResolveJsonCodec());
            Invoke(serviceName, methodName, payloadBase64);
        }

        private static MethodCallExpression ResolveMethodCall(Expression expression, string paramName)
        {
            var methodCall = expression as MethodCallExpression;
            if (methodCall == null)
                throw new ArgumentException("RPC expression must be a method call.", paramName);

            if (string.IsNullOrWhiteSpace(methodCall.Method.Name))
                throw new ArgumentException("Unable to resolve RPC method name from expression.", paramName);

            return methodCall;
        }

        private static string ResolveServiceName<TService>()
        {
            var serviceType = typeof(TService);
            EnsureRpcServiceAttribute(serviceType);
            return serviceType.Name;
        }

        private static void EnsureRpcServiceAttribute(Type serviceType)
        {
            foreach (var attribute in serviceType.GetCustomAttributes(inherit: true))
            {
                if (attribute is RpcAttribute)
                    return;

                var attributeTypeName = attribute.GetType().Name;
                if (string.Equals(attributeTypeName, "RpcAttribute", StringComparison.Ordinal))
                    return;
            }

            throw new InvalidOperationException(
                $"RPC service type '{serviceType.FullName}' must be decorated with [Rpc] attribute.");
        }

        private void HandleModuleCommand(string commandName, object command)
        {
            if (command is InvokeRpcResponse response)
            {
                if (_pendingRequests.TryTake(response, out var pending))
                    pending.Completion.TrySetResult(response);

                OnRpcInvokeResponse?.Invoke(response);
            }
        }
    }
}
