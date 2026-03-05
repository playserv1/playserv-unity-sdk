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
    internal sealed class PlayServDataSubscriptionAdapter : IDataSubscriptionAdapter, IDisposable
    {
        private const int SubscriptionResponseTimeoutMs = 15000;
        private const int DataGetResponseTimeoutMs = 15000;
        private const int DefaultDataGetPollIntervalMs = 4000;
        private readonly PlayServImplementation _transport;
        private readonly ILogger _logger;
        private long _requestIdCounter;
        private readonly IDisposable _commandErrorSubscription;
        private bool _refreshCommandSupported = true;
        private bool _refreshUnsupportedLogged;

        public PlayServDataSubscriptionAdapter(PlayServImplementation proxy, ILogger logger)
        {
            _transport = proxy ?? throw new ArgumentNullException(nameof(proxy));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _commandErrorSubscription = _transport.OnCommand("error", OnCommandErrorReceived);
        }

        public IDisposable OnSubscriptionData(long subscriptionId, Action<object> onData)
        {
            return _transport.On<DataSubscriptionUpdate>(update =>
            {
                if (update is DataSubscriptionUpdate dataUpdate && dataUpdate.DataSubscriptionId == subscriptionId)
                {
                    onData(dataUpdate.Data);
                }
            });
        }

        public IDisposable OnSubscriptionUpdate(long subscriptionId, Action<DataSubscriptionUpdate> onUpdate)
        {
            return _transport.On<DataSubscriptionUpdate>(update =>
            {
                if (update is DataSubscriptionUpdate dataUpdate && dataUpdate.DataSubscriptionId == subscriptionId)
                {
                    onUpdate(dataUpdate);
                }
            });
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

        public void RequestFullState(long subscriptionId)
        {
            _ = RequestFullStateAsync(subscriptionId);
        }

        public Task RequestFullStateAsync(long subscriptionId)
        {
            if (!_refreshCommandSupported)
                return Task.CompletedTask;

            var requestId = Interlocked.Increment(ref _requestIdCounter);
            var refreshRequest = new DataSubscriptionRefreshRequest
            {
                RequestId = requestId,
                SubscriptionId = subscriptionId
            };

            return _transport.SendAsync(refreshRequest, "module_dataflow");
        }

        public async Task<DataSubscriptionResponse> SendSubscriptionRequestAsync(DataSubscriptionRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            var tcs = new TaskCompletionSource<DataSubscriptionResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            IDisposable responseSubscription = null;
            IDisposable errorResponseSubscription = null;
            IDisposable commandErrorSubscription = null;

            responseSubscription = _transport.On<DataSubscriptionResponse>(response =>
            {
                if (response is DataSubscriptionResponse dataResponse && dataResponse.RequestId == request.RequestId)
                {
                    tcs.TrySetResult(dataResponse);
                }
            });

            errorResponseSubscription = _transport.OnCommand("ErrorResponse", command =>
            {
                if (command is ErrorResponse errorResponse)
                {
                    tcs.TrySetResult(CreateErrorResponse(
                        request.RequestId,
                        errorResponse.ErrorCode,
                        errorResponse.Message));
                }
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
                tcs.TrySetResult(CreateErrorResponse(request.RequestId, 0, message));
            });

            try
            {
                await _transport.SendAsync(request, "module_dataflow");
                var timeoutTask = Task.Delay(SubscriptionResponseTimeoutMs);
                var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);
                if (completedTask == timeoutTask)
                {
                    return CreateErrorResponse(
                        request.RequestId,
                        0,
                        $"Timed out waiting for DataSubscriptionResponse after {SubscriptionResponseTimeoutMs}ms.");
                }

                return await tcs.Task;
            }
            finally
            {
                responseSubscription?.Dispose();
                errorResponseSubscription?.Dispose();
                commandErrorSubscription?.Dispose();
            }
        }

        public async Task<ISharedEntity<TDto>> SelectEntity<TEntity, TDto>(string playerId, Func<TEntity, TDto> map)
            where TEntity : class
            where TDto : class, new()
        {
            var entityTypeName = typeof(TEntity).Name;
            var requestId = Interlocked.Increment(ref _requestIdCounter);

            var query = QueryBuilder.BuildQuery<TEntity>(entityTypeName, playerId);
            var variables = QueryBuilder.BuildVariables(playerId);
            _logger.Log($"[DataSubscription] Opening subscription. entity={entityTypeName}, query={query}");

            var request = new DataSubscriptionRequest(requestId, query, variables);
            var response = await SendSubscriptionRequestAsync(request);

            if (response.HasError)
                throw MapErrorToException(response.Error);

            if (response.Result == null)
                throw new InvalidOperationException("Invalid DataSubscriptionResponse: Result is null");

            var subscriptionId = response.Result.SubscriptionId;
            var sharedEntity = new SharedEntity<TDto>(
                this,
                subscriptionId,
                null,
                query,
                variables,
                raw =>
                {
                    var json = raw is string str ? str : JsonConvert.SerializeObject(raw);
                    var entity = JsonConvert.DeserializeObject<TEntity>(json);
                    return map(entity);
                });

            return sharedEntity;
        }

        public async Task<DataGetResponse> GetDataByKeyAsync(
            string key,
            string query,
            Dictionary<string, object> variables,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Key is required.", nameof(key));

            if (string.IsNullOrWhiteSpace(query))
                throw new ArgumentException("Query is required.", nameof(query));

            var requestId = Interlocked.Increment(ref _requestIdCounter);
            var request = new DataGetRequest
            {
                RequestId = requestId,
                Key = key,
                Query = query,
                Variables = CloneVariables(variables)
            };

            return await SendDataGetRequestAsync(request, ct);
        }

        public IDisposable StartDataByKeyPolling(
            string key,
            string query,
            Dictionary<string, object> variables,
            Action<DataGetResponse> onData,
            Action<Exception>? onError = null)
        {
            if (onData == null)
                throw new ArgumentNullException(nameof(onData));

            var cts = new CancellationTokenSource();
            var variablesSnapshot = CloneVariables(variables);

            _ = PollDataByKeyLoopAsync(
                key,
                query,
                variablesSnapshot,
                DefaultDataGetPollIntervalMs,
                onData,
                onError,
                cts.Token);

            return new PollingHandle(cts);
        }

        private static DataSubscriptionException MapErrorToException(DataSubscriptionError error)
        {
            if (error == null)
                return new DataSubscriptionException(0, "Unknown data subscription error.");

            var message = error.Message ?? string.Empty;
            return error.ErrorCode switch
            {
                UpdateDataCorruptionException.Code => new UpdateDataCorruptionException(error.Message),
                SubscriptionTerminatedException.Code => new SubscriptionTerminatedException(0, error.Message),
                SubscriptionNotFoundException.Code => new SubscriptionNotFoundException(0, error.Message),
                0 when LooksLikeInvalidQueryError(message) => new InvalidQuerySyntaxException(message),
                _ => new DataSubscriptionException(error.ErrorCode, error.Message)
            };
        }

        private static DataSubscriptionResponse CreateErrorResponse(long requestId, int errorCode, string message)
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

        private static bool LooksLikeDataSubscriptionCommandError(CommandErrorResponse errorResponse)
        {
            var error = errorResponse?.Error ?? string.Empty;
            var message = errorResponse?.Message ?? string.Empty;

            return message.IndexOf("DataSubscription", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("module_dataflow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("dataflow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("query", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   error.IndexOf("DataSubscription", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool LooksLikeInvalidQueryError(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            return message.IndexOf("Syntax Error", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("GraphQL", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("Query is empty", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("id argument", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private async Task<DataGetResponse> SendDataGetRequestAsync(DataGetRequest request, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<DataGetResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            IDisposable responseSubscription = null;
            IDisposable commandErrorSubscription = null;

            responseSubscription = _transport.On<DataGetResponse>(response =>
            {
                if (response != null && response.RequestId == request.RequestId)
                    tcs.TrySetResult(response);
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

                tcs.TrySetResult(CreateDataGetErrorResponse(request.RequestId, 0, message));
            });

            try
            {
                await _transport.SendAsync(request, "module_dataflow");
                var timeoutTask = Task.Delay(DataGetResponseTimeoutMs, ct);
                var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    if (ct.IsCancellationRequested)
                        throw new OperationCanceledException(ct);

                    return CreateDataGetErrorResponse(
                        request.RequestId,
                        0,
                        $"Timed out waiting for DataGetResponse after {DataGetResponseTimeoutMs}ms.");
                }

                return await tcs.Task;
            }
            finally
            {
                responseSubscription?.Dispose();
                commandErrorSubscription?.Dispose();
            }
        }

        private async Task PollDataByKeyLoopAsync(
            string key,
            string query,
            Dictionary<string, object> variables,
            int intervalMs,
            Action<DataGetResponse> onData,
            Action<Exception>? onError,
            CancellationToken ct)
        {
            _logger.Log($"[DataGet] Polling started. interval={intervalMs}ms, key={key}");

            while (!ct.IsCancellationRequested)
            {
                var startedAt = DateTime.UtcNow;
                try
                {
                    var response = await GetDataByKeyAsync(key, query, variables, ct);
                    onData(response);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[DataGet] Poll request failed: {ex.Message}");
                    onError?.Invoke(ex);
                }

                var elapsedMs = (int)(DateTime.UtcNow - startedAt).TotalMilliseconds;
                var delayMs = Math.Max(0, intervalMs - elapsedMs);

                try
                {
                    await Task.Delay(delayMs, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
            }

            _logger.Log($"[DataGet] Polling stopped. key={key}");
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

        private static bool LooksLikeDataGetCommandError(CommandErrorResponse response)
        {
            var error = response?.Error ?? string.Empty;
            var message = response?.Message ?? string.Empty;

            return message.IndexOf("DataGetRequest", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("DataGet", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("DataGetResponse", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   error.IndexOf("DataGetRequest", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Dictionary<string, object> CloneVariables(Dictionary<string, object> variables)
        {
            if (variables == null || variables.Count == 0)
                return new Dictionary<string, object>();

            return new Dictionary<string, object>(variables);
        }

        public void Dispose()
        {
            _commandErrorSubscription?.Dispose();
        }

        private void OnCommandErrorReceived(object command)
        {
            if (command is not CommandErrorResponse response)
                return;

            if (!IsUnsupportedRefreshCommandError(response))
                return;

            _refreshCommandSupported = false;
            if (_refreshUnsupportedLogged)
                return;

            _refreshUnsupportedLogged = true;
            _logger.LogWarning(
                "[DataSubscription] Server does not support DataSubscriptionRefreshRequest. Refresh requests are disabled for this session.");
        }

        private static bool IsUnsupportedRefreshCommandError(CommandErrorResponse response)
        {
            var message = response?.Message ?? string.Empty;
            if (string.IsNullOrWhiteSpace(message))
                return false;

            return message.IndexOf("DataSubscriptionRefreshRequest", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   message.IndexOf("not supported by this module", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private sealed class PollingHandle : IDisposable
        {
            private CancellationTokenSource? _cts;

            public PollingHandle(CancellationTokenSource cts)
            {
                _cts = cts;
            }

            public void Dispose()
            {
                var cts = Interlocked.Exchange(ref _cts, null);
                if (cts == null)
                    return;

                if (!cts.IsCancellationRequested)
                    cts.Cancel();

                cts.Dispose();
            }
        }
    }
}
