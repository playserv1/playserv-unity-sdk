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
    internal sealed class DataMutationClient
    {
        private readonly IPlayServCommandBus _commandBus;
        private readonly ILogger _logger;
        private readonly DataSubscriptionRequestIdSource _requestIds;
        private readonly IJsonCodec _jsonCodec;

        public DataMutationClient(
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

        public void SendMutation(long subscriptionId, string query, Dictionary<string, object> variables, object patch)
        {
            _ = SendMutationFireAndForgetAsync(subscriptionId, query, variables, patch);
        }

        public async Task SendMutationAsync(long subscriptionId, string query, Dictionary<string, object> variables, object patch)
        {
            if (_commandBus.State != PlayServState.Online)
            {
                throw new DataSubscriptionException(
                    0,
                    $"Data mutation skipped because SDK state is {_commandBus.State}.");
            }

            var mutationRequest = new DataMutationRequest
            {
                RequestId = _requestIds.Next(),
                Query = query,
                Variables = DataSubscriptionRequestSupport.CloneVariables(variables),
                UpdateType = "Overwrite",
                Data = patch
            };

            var response = await SendMutationRequestAsync(mutationRequest);
            var result = response?.Result;
            if (result == null)
                throw new DataSubscriptionException(0, "Invalid DataMutationResponse: Result is missing.");

            if (!result.Success)
                throw DataSubscriptionErrorMapper.MapDataMutationError(result.Error);
        }

        private async Task SendMutationFireAndForgetAsync(long subscriptionId, string query, Dictionary<string, object> variables, object patch)
        {
            try
            {
                await SendMutationAsync(subscriptionId, query, variables, patch);
            }
            catch (Exception ex)
            {
                SafeLogError($"[DataMutation] Fire-and-forget mutation failed: {ex.Message}");
            }
        }

        private async Task<DataMutationResponse> SendMutationRequestAsync(DataMutationRequest request, CancellationToken ct = default)
        {
            var tcs = new TaskCompletionSource<DataMutationResponse>();
            IDisposable typedSubscription = null;
            IDisposable byCommandSubscription = null;
            IDisposable byModuleCommandSubscription = null;
            IDisposable errorResponseSubscription = null;
            IDisposable commandErrorSubscription = null;
            IDisposable commandErrorNamedSubscription = null;
            IDisposable commandErrorRpcSubscription = null;

            typedSubscription = _commandBus.On<DataMutationResponse>(response =>
            {
                if (response != null && response.RequestId == request.RequestId)
                    tcs.TrySetResult(response);
            });

            byCommandSubscription = _commandBus.OnCommand("DataMutationResponse", command =>
            {
                if (!DataSubscriptionRequestSupport.TryMapDataMutationResponse(_jsonCodec, command, out var response))
                    return;

                if (response.RequestId == request.RequestId)
                    tcs.TrySetResult(response);
            });

            byModuleCommandSubscription = _commandBus.OnCommand("module_dataflow.DataMutationResponse", command =>
            {
                if (!DataSubscriptionRequestSupport.TryMapDataMutationResponse(_jsonCodec, command, out var response))
                    return;

                if (response.RequestId == request.RequestId)
                    tcs.TrySetResult(response);
            });

            errorResponseSubscription = _commandBus.OnCommand("ErrorResponse", command =>
            {
                if (command is not ErrorResponse errorResponse)
                    return;

                tcs.TrySetResult(DataSubscriptionRequestSupport.CreateDataMutationErrorResponse(
                    request.RequestId,
                    errorResponse.ErrorCode,
                    errorResponse.Message));
            });

            commandErrorSubscription = _commandBus.OnCommand("error", command =>
            {
                if (command is not CommandErrorResponse errorResponse)
                    return;

                if (!DataSubscriptionRequestSupport.LooksLikeDataMutationCommandError(errorResponse))
                    return;

                var message = DataSubscriptionRequestSupport.FormatCommandErrorMessage(errorResponse);

                tcs.TrySetResult(DataSubscriptionRequestSupport.CreateDataMutationErrorResponse(request.RequestId, 0, message));
            });

            commandErrorNamedSubscription = _commandBus.OnCommand("CommandErrorResponse", command =>
            {
                if (command is not CommandErrorResponse errorResponse)
                    return;

                if (!DataSubscriptionRequestSupport.LooksLikeDataMutationCommandError(errorResponse))
                    return;

                var message = DataSubscriptionRequestSupport.FormatCommandErrorMessage(errorResponse);

                tcs.TrySetResult(DataSubscriptionRequestSupport.CreateDataMutationErrorResponse(request.RequestId, 0, message));
            });

            commandErrorRpcSubscription = _commandBus.OnCommand("RpcErrorResponse", command =>
            {
                if (command is not CommandErrorResponse errorResponse)
                    return;

                if (!DataSubscriptionRequestSupport.LooksLikeDataMutationCommandError(errorResponse))
                    return;

                var message = DataSubscriptionRequestSupport.FormatCommandErrorMessage(errorResponse);

                tcs.TrySetResult(DataSubscriptionRequestSupport.CreateDataMutationErrorResponse(request.RequestId, 0, message));
            });

            try
            {
                await _commandBus.SendAsync(request, "module_dataflow");
                var completedInTime = await DataSubscriptionRequestSupport.WaitForCompletionOrTimeoutAsync(
                    tcs.Task,
                    PlayServDataSubscriptionAdapter.DataMutationResponseTimeoutMs,
                    ct);

                if (!completedInTime)
                {
                    throw new DataSubscriptionException(
                        0,
                        $"Timed out waiting for DataMutationResponse after {PlayServDataSubscriptionAdapter.DataMutationResponseTimeoutMs}ms.");
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

        private void SafeLogError(string message)
        {
            try
            {
                _logger.LogError(message);
            }
            catch
            {
            }
        }
    }
}
