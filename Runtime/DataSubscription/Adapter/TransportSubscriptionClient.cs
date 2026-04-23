using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Playserv.DataSubscription.Exceptions;
using Playserv.DataSubscription.Requests;
using Playserv.DataSubscription.Responses;
using Playserv.Events.Responses;
using Playserv.Proxy.Common;
using ILogger = Playserv.Proxy.Logging.ILogger;

namespace Playserv.DataSubscription
{
    internal sealed class TransportSubscriptionClient
    {
        private readonly PlayServImplementation _transport;
        private readonly ILogger _logger;
        private readonly DataSubscriptionRequestIdSource _requestIds;

        public TransportSubscriptionClient(
            PlayServImplementation transport,
            ILogger logger,
            DataSubscriptionRequestIdSource requestIds)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _requestIds = requestIds ?? throw new ArgumentNullException(nameof(requestIds));
        }

        public async Task<long?> TryOpenTransportSubscriptionAsync(
            string query,
            Dictionary<string, object> variables,
            bool allowFallbackToPolling = true,
            CancellationToken ct = default)
        {
            var request = new DataSubscriptionRequest(
                _requestIds.Next(),
                query,
                DataSubscriptionRequestSupport.CloneVariables(variables));

            var response = await SendSubscriptionRequestAsync(request, ct);

            if (response.HasError)
            {
                var mapped = DataSubscriptionErrorMapper.MapErrorToException(response.Error);
                if (allowFallbackToPolling && DataSubscriptionErrorMapper.ShouldFallbackToPolling(mapped))
                {
                    SafeLogWarning(
                        $"[DataSubscription] Transport subscribe unavailable. Falling back to polling. reason={mapped.Message}");
                    return null;
                }

                throw mapped;
            }

            var subscriptionId = response.Result?.SubscriptionId ?? 0;
            if (subscriptionId <= 0)
            {
                if (allowFallbackToPolling)
                {
                    SafeLogWarning("[DataSubscription] Transport subscribe returned empty subscription id. Falling back to polling.");
                    return null;
                }

                throw new DataSubscriptionException(0, "Invalid DataSubscriptionResponse: Result.SubscriptionId is empty.");
            }

            return subscriptionId;
        }

        private async Task<DataSubscriptionResponse> SendSubscriptionRequestAsync(
            DataSubscriptionRequest request,
            CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<DataSubscriptionResponse>();
            IDisposable typedSubscription = null;
            IDisposable byCommandSubscription = null;
            IDisposable byModuleCommandSubscription = null;
            IDisposable errorResponseSubscription = null;
            IDisposable commandErrorSubscription = null;
            IDisposable commandErrorNamedSubscription = null;
            IDisposable commandErrorRpcSubscription = null;

            typedSubscription = _transport.On<DataSubscriptionResponse>(response =>
            {
                if (response != null && response.RequestId == request.RequestId)
                    tcs.TrySetResult(response);
            });

            byCommandSubscription = _transport.OnCommand("DataSubscriptionResponse", command =>
            {
                if (!DataSubscriptionRequestSupport.TryMapDataSubscriptionResponse(command, out var response))
                    return;

                if (response.RequestId == request.RequestId)
                    tcs.TrySetResult(response);
            });

            byModuleCommandSubscription = _transport.OnCommand("module_dataflow.DataSubscriptionResponse", command =>
            {
                if (!DataSubscriptionRequestSupport.TryMapDataSubscriptionResponse(command, out var response))
                    return;

                if (response.RequestId == request.RequestId)
                    tcs.TrySetResult(response);
            });

            errorResponseSubscription = _transport.OnCommand("ErrorResponse", command =>
            {
                if (command is not ErrorResponse errorResponse)
                    return;

                tcs.TrySetResult(new DataSubscriptionResponse
                {
                    RequestId = request.RequestId,
                    Error = new DataSubscriptionError
                    {
                        ErrorCode = errorResponse.ErrorCode,
                        Message = errorResponse.Message
                    }
                });
            });

            commandErrorSubscription = _transport.OnCommand("error", command =>
            {
                if (command is not CommandErrorResponse errorResponse)
                    return;

                if (!DataSubscriptionRequestSupport.LooksLikeDataSubscriptionCommandError(errorResponse))
                    return;

                var message = string.IsNullOrWhiteSpace(errorResponse.Message)
                    ? errorResponse.Error
                    : errorResponse.Message;

                tcs.TrySetResult(DataSubscriptionRequestSupport.CreateDataSubscriptionErrorResponse(request.RequestId, 0, message));
            });

            commandErrorNamedSubscription = _transport.OnCommand("CommandErrorResponse", command =>
            {
                if (command is not CommandErrorResponse errorResponse)
                    return;

                if (!DataSubscriptionRequestSupport.LooksLikeDataSubscriptionCommandError(errorResponse))
                    return;

                var message = string.IsNullOrWhiteSpace(errorResponse.Message)
                    ? errorResponse.Error
                    : errorResponse.Message;

                tcs.TrySetResult(DataSubscriptionRequestSupport.CreateDataSubscriptionErrorResponse(request.RequestId, 0, message));
            });

            commandErrorRpcSubscription = _transport.OnCommand("RpcErrorResponse", command =>
            {
                if (command is not CommandErrorResponse errorResponse)
                    return;

                if (!DataSubscriptionRequestSupport.LooksLikeDataSubscriptionCommandError(errorResponse))
                    return;

                var message = string.IsNullOrWhiteSpace(errorResponse.Message)
                    ? errorResponse.Error
                    : errorResponse.Message;

                tcs.TrySetResult(DataSubscriptionRequestSupport.CreateDataSubscriptionErrorResponse(request.RequestId, 0, message));
            });

            try
            {
                await _transport.SendAsync(request, "module_dataflow");
                var completedInTime = await DataSubscriptionRequestSupport.WaitForCompletionOrTimeoutAsync(
                    tcs.Task,
                    PlayServDataSubscriptionAdapter.DataSubscriptionResponseTimeoutMs,
                    ct);

                if (!completedInTime)
                {
                    return DataSubscriptionRequestSupport.CreateDataSubscriptionErrorResponse(
                        request.RequestId,
                        0,
                        $"Timed out waiting for DataSubscriptionResponse after {PlayServDataSubscriptionAdapter.DataSubscriptionResponseTimeoutMs}ms.");
                }

                return await tcs.Task;
            }
            finally
            {
                typedSubscription?.Dispose();
                byCommandSubscription?.Dispose();
                byModuleCommandSubscription?.Dispose();
                errorResponseSubscription?.Dispose();
                commandErrorSubscription?.Dispose();
                commandErrorNamedSubscription?.Dispose();
                commandErrorRpcSubscription?.Dispose();
            }
        }

        private void SafeLogWarning(string message)
        {
            try
            {
                _logger.LogWarning(message);
            }
            catch
            {
            }
        }
    }
}
