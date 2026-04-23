using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Playserv.DataSubscription.Exceptions;
using Playserv.DataSubscription.Requests;
using Playserv.DataSubscription.Responses;
using Playserv.Events.Responses;
using Playserv.Proxy.Common;
using ILogger = Playserv.Proxy.Logging.ILogger;

namespace Playserv.DataSubscription
{
    internal sealed class DataSubscriptionRequestClient
    {
        private readonly PlayServImplementation _transport;
        private readonly ILogger _logger;
        private long _requestIdCounter;

        public DataSubscriptionRequestClient(PlayServImplementation transport, ILogger logger)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<long?> TryOpenTransportSubscriptionAsync(
            string query,
            Dictionary<string, object> variables,
            bool allowFallbackToPolling = true,
            CancellationToken ct = default)
        {
            var request = new DataSubscriptionRequest(
                Interlocked.Increment(ref _requestIdCounter),
                query,
                CloneVariables(variables));

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

        public void SendMutation(long subscriptionId, string query, Dictionary<string, object> variables, object patch)
        {
            _ = SendMutationAsync(subscriptionId, query, variables, patch);
        }

        public async Task SendMutationAsync(long subscriptionId, string query, Dictionary<string, object> variables, object patch)
        {
            var requestId = Interlocked.Increment(ref _requestIdCounter);
            var mutationRequest = new DataMutationRequest
            {
                RequestId = requestId,
                Query = query,
                Variables = variables,
                UpdateType = "Overwrite",
                Data = patch
            };

            await _transport.SendAsync(mutationRequest, "module_dataflow");
        }

        public async Task<DataGetResponse> GetDataByKeyAsync(
            string key,
            string query,
            Dictionary<string, object> variables,
            int timeoutMs,
            CancellationToken ct = default)
        {
            var request = CreateDataGetRequest(key, query, variables);
            return await SendDataGetRequestAsync(request, timeoutMs, ct);
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
                if (!TryMapDataSubscriptionResponse(command, out var response))
                    return;

                if (response.RequestId == request.RequestId)
                    tcs.TrySetResult(response);
            });

            byModuleCommandSubscription = _transport.OnCommand("module_dataflow.DataSubscriptionResponse", command =>
            {
                if (!TryMapDataSubscriptionResponse(command, out var response))
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

                if (!LooksLikeDataSubscriptionCommandError(errorResponse))
                    return;

                var message = string.IsNullOrWhiteSpace(errorResponse.Message)
                    ? errorResponse.Error
                    : errorResponse.Message;

                tcs.TrySetResult(CreateDataSubscriptionErrorResponse(request.RequestId, 0, message));
            });

            commandErrorNamedSubscription = _transport.OnCommand("CommandErrorResponse", command =>
            {
                if (command is not CommandErrorResponse errorResponse)
                    return;

                if (!LooksLikeDataSubscriptionCommandError(errorResponse))
                    return;

                var message = string.IsNullOrWhiteSpace(errorResponse.Message)
                    ? errorResponse.Error
                    : errorResponse.Message;

                tcs.TrySetResult(CreateDataSubscriptionErrorResponse(request.RequestId, 0, message));
            });

            commandErrorRpcSubscription = _transport.OnCommand("RpcErrorResponse", command =>
            {
                if (command is not CommandErrorResponse errorResponse)
                    return;

                if (!LooksLikeDataSubscriptionCommandError(errorResponse))
                    return;

                var message = string.IsNullOrWhiteSpace(errorResponse.Message)
                    ? errorResponse.Error
                    : errorResponse.Message;

                tcs.TrySetResult(CreateDataSubscriptionErrorResponse(request.RequestId, 0, message));
            });

            try
            {
                await _transport.SendAsync(request, "module_dataflow");
                var completedInTime = await WaitForCompletionOrTimeoutAsync(
                    tcs.Task,
                    PlayServDataSubscriptionAdapter.DataSubscriptionResponseTimeoutMs,
                    ct);

                if (!completedInTime)
                {
                    return CreateDataSubscriptionErrorResponse(
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

            responseSubscription = _transport.On<DataGetResponse>(response =>
            {
                if (response != null && response.RequestId == request.RequestId)
                {
                    var set = tcs.TrySetResult(response);
#if PlayServ_Logs
                    SafeLog($"[DataGet] typed response matched. requestId={request.RequestId}, setResult={set}, hasError={response.HasError}");
#endif
                }
            });

            responseByCommandSubscription = _transport.OnCommand("DataGetResponse", command =>
            {
                if (!TryMapDataGetResponse(command, out var response))
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

            responseByModuleCommandSubscription = _transport.OnCommand("module_dataflow.DataGetResponse", command =>
            {
                if (!TryMapDataGetResponse(command, out var response))
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

            commandErrorSubscription = _transport.OnCommand("error", command =>
            {
                if (command is not CommandErrorResponse errorResponse)
                    return;

                if (!LooksLikeDataGetCommandError(errorResponse))
                    return;

                var message = string.IsNullOrWhiteSpace(errorResponse.Message)
                    ? errorResponse.Error
                    : errorResponse.Message;

                var set = tcs.TrySetResult(CreateDataGetErrorResponse(request.RequestId, 0, message));
#if PlayServ_Logs
                SafeLog($"[DataGet] command error mapped to requestId={request.RequestId}, setResult={set}, message={message}");
#endif
            });

            commandErrorNamedSubscription = _transport.OnCommand("CommandErrorResponse", command =>
            {
                if (command is not CommandErrorResponse errorResponse)
                    return;

                if (!LooksLikeDataGetCommandError(errorResponse))
                    return;

                var message = string.IsNullOrWhiteSpace(errorResponse.Message)
                    ? errorResponse.Error
                    : errorResponse.Message;

                var set = tcs.TrySetResult(CreateDataGetErrorResponse(request.RequestId, 0, message));
#if PlayServ_Logs
                SafeLog($"[DataGet] named command error mapped to requestId={request.RequestId}, setResult={set}, message={message}");
#endif
            });

            commandErrorRpcSubscription = _transport.OnCommand("RpcErrorResponse", command =>
            {
                if (command is not CommandErrorResponse errorResponse)
                    return;

                if (!LooksLikeDataGetCommandError(errorResponse))
                    return;

                var message = string.IsNullOrWhiteSpace(errorResponse.Message)
                    ? errorResponse.Error
                    : errorResponse.Message;

                var set = tcs.TrySetResult(CreateDataGetErrorResponse(request.RequestId, 0, message));
#if PlayServ_Logs
                SafeLog($"[DataGet] rpc error mapped to requestId={request.RequestId}, setResult={set}, message={message}");
#endif
            });

            try
            {
                await _transport.SendAsync(request, "module_dataflow");
                var effectiveTimeoutMs = timeoutMs > 0 ? timeoutMs : PlayServDataSubscriptionAdapter.DataGetResponseTimeoutMs;
                var completedInTime = await WaitForCompletionOrTimeoutAsync(tcs.Task, effectiveTimeoutMs, ct);
                if (!completedInTime)
                {
#if PlayServ_Logs
                    SafeLogWarning($"[DataGet] timeout. requestId={request.RequestId}, key={request.Key}, timeout={effectiveTimeoutMs}ms");
#endif
                    return CreateDataGetErrorResponse(
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

        private DataGetRequest CreateDataGetRequest(
            string key,
            string query,
            Dictionary<string, object> variables)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Key is required.", nameof(key));

            if (string.IsNullOrWhiteSpace(query))
                throw new ArgumentException("Query is required.", nameof(query));

            return new DataGetRequest
            {
                RequestId = Interlocked.Increment(ref _requestIdCounter),
                Key = key,
                Query = query,
                Variables = CloneVariables(variables)
            };
        }

        private static bool TryMapDataGetResponse(object command, out DataGetResponse response)
        {
            response = null;
            if (command == null)
                return false;

            if (command is DataGetResponse dataGetResponse)
            {
                response = dataGetResponse;
                return true;
            }

            try
            {
                var json = JsonConvert.SerializeObject(command);
                var mapped = JsonConvert.DeserializeObject<DataGetResponse>(json);
                if (mapped == null)
                    return false;

                response = mapped;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryMapDataSubscriptionResponse(object command, out DataSubscriptionResponse response)
        {
            response = null;
            if (command == null)
                return false;

            if (command is DataSubscriptionResponse typed)
            {
                response = typed;
                return true;
            }

            try
            {
                var json = JsonConvert.SerializeObject(command);
                var mapped = JsonConvert.DeserializeObject<DataSubscriptionResponse>(json);
                if (mapped == null)
                    return false;

                response = mapped;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool LooksLikeDataSubscriptionCommandError(CommandErrorResponse response)
        {
            var error = response?.Error ?? string.Empty;
            var message = response?.Message ?? string.Empty;

            return message.IndexOf("DataSubscriptionRequest", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("DataSubscription", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   error.IndexOf("DataSubscriptionRequest", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   error.IndexOf("DataSubscription", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool LooksLikeDataGetCommandError(CommandErrorResponse response)
        {
            var error = response?.Error ?? string.Empty;
            var message = response?.Message ?? string.Empty;

            return message.IndexOf("DataGetRequest", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("DataGet", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("DataGetResponse", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   error.IndexOf("DataGetRequest", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static DataGetResponse CreateDataGetErrorResponse(long requestId, int errorCode, string message)
        {
            return new DataGetResponse
            {
                RequestId = requestId,
                Error = new DataGetError
                {
                    Code = errorCode,
                    Message = message ?? "Unknown data get error."
                }
            };
        }

        private static DataSubscriptionResponse CreateDataSubscriptionErrorResponse(long requestId, int errorCode, string message)
        {
            return new DataSubscriptionResponse
            {
                RequestId = requestId,
                Error = new DataSubscriptionError
                {
                    ErrorCode = errorCode,
                    Message = message ?? "Unknown data subscription error."
                }
            };
        }

        private static Dictionary<string, object> CloneVariables(Dictionary<string, object> variables)
        {
            if (variables == null || variables.Count == 0)
                return new Dictionary<string, object>();

            return new Dictionary<string, object>(variables);
        }

        private static async Task<bool> WaitForCompletionOrTimeoutAsync(Task task, int timeoutMs, CancellationToken ct)
        {
            if (task.IsCompleted)
                return true;

            if (timeoutMs <= 0)
                timeoutMs = 1;

#if UNITY_WEBGL && !UNITY_EDITOR
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            while (!task.IsCompleted)
            {
                if (ct.IsCancellationRequested)
                    throw new OperationCanceledException(ct);

                if (stopwatch.ElapsedMilliseconds >= timeoutMs)
                    return false;

                await Task.Yield();
            }

            return true;
#else
            var timeoutTask = Task.Delay(timeoutMs, ct);
            var completedTask = await Task.WhenAny(task, timeoutTask);
            if (completedTask == task)
                return true;

            if (ct.IsCancellationRequested)
                throw new OperationCanceledException(ct);

            return false;
#endif
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
