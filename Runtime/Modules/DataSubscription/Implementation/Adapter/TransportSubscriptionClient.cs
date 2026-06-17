using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Playserv.DataSubscription.Exceptions;
using Playserv.DataSubscription.Requests;
using Playserv.DataSubscription.Responses;
using Playserv.Modules;
using Playserv.Proxy.Common;
using Playserv.Serialization;
using ILogger = Playserv.Proxy.Logging.ILogger;

namespace Playserv.DataSubscription
{
    internal sealed class TransportSubscriptionClient
    {
        private readonly IPlayServCommandBus _commandBus;
        private readonly ILogger _logger;
        private readonly DataSubscriptionRequestIdSource _requestIds;
        private readonly IJsonCodec _jsonCodec;

        public TransportSubscriptionClient(
            IPlayServCommandBus commandBus,
            ILogger logger,
            DataSubscriptionRequestIdSource requestIds,
            IJsonCodec jsonCodec)
        {
            _commandBus = commandBus ?? throw new ArgumentNullException(nameof(commandBus));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _requestIds = requestIds ?? throw new ArgumentNullException(nameof(requestIds));
            _jsonCodec = jsonCodec ?? throw new ArgumentNullException(nameof(jsonCodec));
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

            typedSubscription = _commandBus.On<DataSubscriptionResponse>(response =>
            {
                if (response != null && response.RequestId == request.RequestId)
                    tcs.TrySetResult(response);
            });

            byCommandSubscription = _commandBus.OnCommand("DataSubscriptionResponse", command =>
            {
                if (!DataSubscriptionRequestSupport.TryMapDataSubscriptionResponse(_jsonCodec, command, out var response))
                    return;

                if (response.RequestId == request.RequestId)
                    tcs.TrySetResult(response);
            });

            byModuleCommandSubscription = _commandBus.OnCommand("module_dataflow.DataSubscriptionResponse", command =>
            {
                if (!DataSubscriptionRequestSupport.TryMapDataSubscriptionResponse(_jsonCodec, command, out var response))
                    return;

                if (response.RequestId == request.RequestId)
                    tcs.TrySetResult(response);
            });

            errorResponseSubscription = _commandBus.OnCommand("ErrorResponse", command =>
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

            commandErrorSubscription = _commandBus.OnCommand("error", command =>
            {
                if (command is not CommandErrorResponse errorResponse)
                    return;

                if (!DataSubscriptionRequestSupport.LooksLikeDataSubscriptionCommandError(errorResponse))
                    return;

                var message = DataSubscriptionRequestSupport.FormatCommandErrorMessage(errorResponse);

                tcs.TrySetResult(DataSubscriptionRequestSupport.CreateDataSubscriptionErrorResponse(request.RequestId, 0, message));
            });

            commandErrorNamedSubscription = _commandBus.OnCommand("CommandErrorResponse", command =>
            {
                if (command is not CommandErrorResponse errorResponse)
                    return;

                if (!DataSubscriptionRequestSupport.LooksLikeDataSubscriptionCommandError(errorResponse))
                    return;

                var message = DataSubscriptionRequestSupport.FormatCommandErrorMessage(errorResponse);

                tcs.TrySetResult(DataSubscriptionRequestSupport.CreateDataSubscriptionErrorResponse(request.RequestId, 0, message));
            });

            commandErrorRpcSubscription = _commandBus.OnCommand("RpcErrorResponse", command =>
            {
                if (command is not CommandErrorResponse errorResponse)
                    return;

                if (!DataSubscriptionRequestSupport.LooksLikeDataSubscriptionCommandError(errorResponse))
                    return;

                var message = DataSubscriptionRequestSupport.FormatCommandErrorMessage(errorResponse);

                tcs.TrySetResult(DataSubscriptionRequestSupport.CreateDataSubscriptionErrorResponse(request.RequestId, 0, message));
            });

            try
            {
                await _commandBus.SendAsync(request, "module_dataflow");
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
