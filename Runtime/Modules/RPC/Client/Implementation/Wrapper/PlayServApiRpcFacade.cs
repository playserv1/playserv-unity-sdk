using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Playserv.Proxy.Common;
using Playserv.RPC;
#if !PLAYSERV_MODULE_DISABLED_LOCAL_EXECUTION_CORE
using Playserv.Server;
#endif
using Playserv.Serialization;

namespace Playserv.Wrapper
{
    internal sealed class PlayServApiRpcFacade : IPlayServRpcApi
    {
#if !PLAYSERV_MODULE_DISABLED_LOCAL_EXECUTION_CORE
        private readonly IPlayServLocalExecution _localExecution;
#endif
        private readonly Func<PlayServImplementation> _getCurrentInstance;
        private readonly Func<string, PlayServImplementation> _getInstanceForFireAndForget;
        private readonly Func<IJsonCodec> _getJsonCodec;

        public event Action<InvokeRpcResponse> OnRpcInvokeResponse;

        public PlayServApiRpcFacade()
            : this(
#if !PLAYSERV_MODULE_DISABLED_LOCAL_EXECUTION_CORE
                (IPlayServLocalExecution)PlayServRuntimeHost.LocalExecution,
#endif
                () => PlayServRuntimeHost.CurrentInstance,
                PlayServRuntimeHost.GetInstanceForFireAndForget,
                PlayServRuntimeHost.ResolveJsonCodec)
        {
            PlayServRuntimeHost.ModuleCommandReceived += HandleModuleCommand;
        }

        public PlayServApiRpcFacade(
#if !PLAYSERV_MODULE_DISABLED_LOCAL_EXECUTION_CORE
            IPlayServLocalExecution localExecution,
#endif
            Func<PlayServImplementation> getCurrentInstance,
            Func<string, PlayServImplementation> getInstanceForFireAndForget,
            Func<IJsonCodec> getJsonCodec)
        {
#if !PLAYSERV_MODULE_DISABLED_LOCAL_EXECUTION_CORE
            _localExecution = localExecution ?? throw new ArgumentNullException(nameof(localExecution));
#endif
            _getCurrentInstance = getCurrentInstance ?? throw new ArgumentNullException(nameof(getCurrentInstance));
            _getInstanceForFireAndForget = getInstanceForFireAndForget ?? throw new ArgumentNullException(nameof(getInstanceForFireAndForget));
            _getJsonCodec = getJsonCodec ?? throw new ArgumentNullException(nameof(getJsonCodec));
        }

#if !PLAYSERV_MODULE_DISABLED_LOCAL_EXECUTION_CORE && !PLAYSERV_MODULE_DISABLED_LOCAL_EXECUTION_SERVER
        public void SetCommandHandler(ICommandHandler commandHandler) =>
            _localExecution.SetCommandHandler(commandHandler);
#endif

        public void Send<T>(T command) => PlayServRuntimeHost.Send(command);

        public void Send<T>(T command, string moduleName) => PlayServRuntimeHost.Send(command, moduleName);

        public void Invoke(string serviceName, string methodName, object payload)
        {
            var payloadBase64 = RpcPayloadSerializer.SerializeToBase64(payload, _getJsonCodec());
            Invoke(serviceName, methodName, payloadBase64);
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
        {
            if (string.IsNullOrWhiteSpace(serviceName))
                throw new ArgumentException("Service name is required.", nameof(serviceName));

            if (string.IsNullOrWhiteSpace(methodName))
                throw new ArgumentException("Method name is required.", nameof(methodName));

            if (string.IsNullOrWhiteSpace(payloadBase64))
                throw new ArgumentException("Payload base64 is required.", nameof(payloadBase64));

#if !PLAYSERV_MODULE_DISABLED_LOCAL_EXECUTION_CORE
            if (_localExecution.TryInvokeRpc(serviceName, methodName, payloadBase64, _getCurrentInstance() != null))
                return;
#endif

            var request = new InvokeRpc
            {
                ServiceName = serviceName,
                MethodName = methodName,
                Payload = payloadBase64
            };

            var instance = _getInstanceForFireAndForget("RPC invoke");
            if (instance == null)
                return;

            instance.Send(request, RpcConstants.InvokeModuleServiceName);
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
            var payloadBase64 = RpcPayloadSerializer.SerializeToBase64(payload, _getJsonCodec());
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
                OnRpcInvokeResponse?.Invoke(response);
        }
    }
}
