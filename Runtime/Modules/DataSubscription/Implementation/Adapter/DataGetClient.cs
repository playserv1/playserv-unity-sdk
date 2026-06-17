using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Playserv.DataSubscription.Requests;
using Playserv.DataSubscription.Responses;
using Playserv.Modules;
using Playserv.Proxy.Common;
using Playserv.Serialization;
using ILogger = Playserv.Proxy.Logging.ILogger;

namespace Playserv.DataSubscription
{
    internal sealed class DataGetClient
    {
        private readonly IPlayServCommandBus _commandBus;
        private readonly ILogger _logger;
        private readonly DataSubscriptionRequestIdSource _requestIds;
        private readonly IJsonCodec _jsonCodec;

        public DataGetClient(
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

        public async Task<DataGetResponse> GetDataByKeyAsync(
            string key,
            string query,
            Dictionary<string, object> variables,
            int timeoutMs,
            CancellationToken ct = default)
        {
            var request = DataSubscriptionRequestSupport.CreateDataGetRequest(_requestIds, key, query, variables);
            return await SendDataGetRequestAsync(request, timeoutMs, ct);
        }

        private async Task<DataGetResponse> SendDataGetRequestAsync(
            DataGetRequest request,
            int timeoutMs,
            CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<DataGetResponse>();
            IDisposable responseSubscription = null;
            IDisposable responseByCommandSubscription = null;
            IDisposable responseByModuleCommandSubscription = null;
            IDisposable commandErrorSubscription = null;
            IDisposable commandErrorNamedSubscription = null;
            IDisposable commandErrorRpcSubscription = null;

            responseSubscription = _commandBus.On<DataGetResponse>(response =>
            {
                if (response != null && response.RequestId == request.RequestId)
                {
                    var set = tcs.TrySetResult(response);
#if PlayServ_Logs
                    SafeLog($"[DataGet] typed response matched. requestId={request.RequestId}, setResult={set}, hasError={response.HasError}");
#endif
                }
            });

            responseByCommandSubscription = _commandBus.OnCommand("DataGetResponse", command =>
            {
                if (!DataSubscriptionRequestSupport.TryMapDataGetResponse(_jsonCodec, command, out var response))
                    return;

#if PlayServ_Logs
                SafeLog($"[DataGet] command response observed. expected={request.RequestId}, incoming={response.RequestId}, hasError={response.HasError}");
#endif

                if (response.RequestId == request.RequestId)
                {
                    var set = tcs.TrySetResult(response);
#if PlayServ_Logs
                    SafeLog($"[DataGet] command response matched. requestId={request.RequestId}, setResult={set}");
#endif
                }
            });

            responseByModuleCommandSubscription = _commandBus.OnCommand("module_dataflow.DataGetResponse", command =>
            {
                if (!DataSubscriptionRequestSupport.TryMapDataGetResponse(_jsonCodec, command, out var response))
                    return;

#if PlayServ_Logs
                SafeLog($"[DataGet] module response observed. expected={request.RequestId}, incoming={response.RequestId}, hasError={response.HasError}");
#endif

                if (response.RequestId == request.RequestId)
                {
                    var set = tcs.TrySetResult(response);
#if PlayServ_Logs
                    SafeLog($"[DataGet] module response matched. requestId={request.RequestId}, setResult={set}");
#endif
                }
            });

            commandErrorSubscription = _commandBus.OnCommand("error", command =>
            {
                if (command is not CommandErrorResponse errorResponse)
                    return;

                if (!DataSubscriptionRequestSupport.LooksLikeDataGetCommandError(errorResponse))
                    return;

                var message = DataSubscriptionRequestSupport.FormatCommandErrorMessage(errorResponse);

                var set = tcs.TrySetResult(DataSubscriptionRequestSupport.CreateDataGetErrorResponse(request.RequestId, 0, message));
#if PlayServ_Logs
                SafeLog($"[DataGet] command error mapped to requestId={request.RequestId}, setResult={set}, message={message}");
#endif
            });

            commandErrorNamedSubscription = _commandBus.OnCommand("CommandErrorResponse", command =>
            {
                if (command is not CommandErrorResponse errorResponse)
                    return;

                if (!DataSubscriptionRequestSupport.LooksLikeDataGetCommandError(errorResponse))
                    return;

                var message = DataSubscriptionRequestSupport.FormatCommandErrorMessage(errorResponse);

                var set = tcs.TrySetResult(DataSubscriptionRequestSupport.CreateDataGetErrorResponse(request.RequestId, 0, message));
#if PlayServ_Logs
                SafeLog($"[DataGet] named command error mapped to requestId={request.RequestId}, setResult={set}, message={message}");
#endif
            });

            commandErrorRpcSubscription = _commandBus.OnCommand("RpcErrorResponse", command =>
            {
                if (command is not CommandErrorResponse errorResponse)
                    return;

                if (!DataSubscriptionRequestSupport.LooksLikeDataGetCommandError(errorResponse))
                    return;

                var message = DataSubscriptionRequestSupport.FormatCommandErrorMessage(errorResponse);

                var set = tcs.TrySetResult(DataSubscriptionRequestSupport.CreateDataGetErrorResponse(request.RequestId, 0, message));
#if PlayServ_Logs
                SafeLog($"[DataGet] rpc error mapped to requestId={request.RequestId}, setResult={set}, message={message}");
#endif
            });

            try
            {
                await _commandBus.SendAsync(request, "module_dataflow");
                var effectiveTimeoutMs = timeoutMs > 0 ? timeoutMs : PlayServDataSubscriptionAdapter.DataGetResponseTimeoutMs;
                var completedInTime = await DataSubscriptionRequestSupport.WaitForCompletionOrTimeoutAsync(tcs.Task, effectiveTimeoutMs, ct);
                if (!completedInTime)
                {
#if PlayServ_Logs
                    SafeLogWarning($"[DataGet] timeout. requestId={request.RequestId}, key={request.Key}, timeout={effectiveTimeoutMs}ms");
#endif
                    return DataSubscriptionRequestSupport.CreateDataGetErrorResponse(
                        request.RequestId,
                        0,
                        $"Timed out waiting for DataGetResponse after {effectiveTimeoutMs}ms.");
                }

                return await tcs.Task;
            }
            finally
            {
                responseSubscription?.Dispose();
                responseByCommandSubscription?.Dispose();
                responseByModuleCommandSubscription?.Dispose();
                commandErrorSubscription?.Dispose();
                commandErrorNamedSubscription?.Dispose();
                commandErrorRpcSubscription?.Dispose();
            }
        }

        private void SafeLog(string message)
        {
            try
            {
                _logger.Log(message);
            }
            catch
            {
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
