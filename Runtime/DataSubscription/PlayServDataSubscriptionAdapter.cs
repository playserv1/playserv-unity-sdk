using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
        private const int DataGetResponseTimeoutMs = 15000;
        private const int DefaultDataGetPollIntervalMs = 4000;
        private const int DefaultDataGetPollRequestTimeoutMs = 4000;
        private const int SubscriptionPollIntervalMs = 3000;
        private const int SubscriptionPollRequestTimeoutMs = 4000;

        private readonly PlayServImplementation _transport;
        private readonly ILogger _logger;
        private readonly object _subscriptionRegistryGate = new object();
        private readonly Dictionary<long, PollingSubscriptionEntry> _subscriptionRegistry = new Dictionary<long, PollingSubscriptionEntry>();
        private long _requestIdCounter;
        private long _subscriptionIdCounter;

        public PlayServDataSubscriptionAdapter(PlayServImplementation proxy, ILogger logger)
        {
            _transport = proxy ?? throw new ArgumentNullException(nameof(proxy));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public IDisposable OnSubscriptionData(long subscriptionId, Action<object> onData)
        {
            return _transport.On<DataSubscriptionUpdate>(update =>
            {
                if (update is DataSubscriptionUpdate dataUpdate && dataUpdate.DataSubscriptionId == subscriptionId)
                    onData(dataUpdate.Data);
            });
        }

        public IDisposable OnSubscriptionUpdate(long subscriptionId, Action<DataSubscriptionUpdate> onUpdate)
        {
            return _transport.On<DataSubscriptionUpdate>(update =>
            {
                if (update is DataSubscriptionUpdate dataUpdate && dataUpdate.DataSubscriptionId == subscriptionId)
                    onUpdate(dataUpdate);
            });
        }

        internal long NextSubscriptionId()
        {
            return Interlocked.Increment(ref _subscriptionIdCounter);
        }

        internal IDisposable RegisterPollingSubscription(
            long subscriptionId,
            string key,
            string query,
            Dictionary<string, object> variables,
            string rootFieldName,
            Action<object?> onChanged,
            Action<DataSubscriptionException>? onError = null)
        {
            if (subscriptionId <= 0)
                throw new ArgumentOutOfRangeException(nameof(subscriptionId));

            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Key is required.", nameof(key));

            if (string.IsNullOrWhiteSpace(query))
                throw new ArgumentException("Query is required.", nameof(query));

            if (onChanged == null)
                throw new ArgumentNullException(nameof(onChanged));

            var entry = new PollingSubscriptionEntry(
                subscriptionId,
                key,
                query,
                CloneVariables(variables),
                rootFieldName ?? string.Empty,
                onChanged,
                onError);

            lock (_subscriptionRegistryGate)
            {
                if (_subscriptionRegistry.ContainsKey(subscriptionId))
                    throw new InvalidOperationException($"Subscription with id={subscriptionId} is already registered.");

                _subscriptionRegistry[subscriptionId] = entry;
            }

            var pollingHandle = StartDataByKeyPollingInternal(
                key,
                query,
                entry.Variables,
                SubscriptionPollIntervalMs,
                SubscriptionPollRequestTimeoutMs,
                response => ProcessSubscriptionResponse(subscriptionId, response),
                ex => ProcessSubscriptionException(subscriptionId, ex));

            lock (_subscriptionRegistryGate)
            {
                if (_subscriptionRegistry.TryGetValue(subscriptionId, out var existing))
                {
                    existing.PollingHandle = pollingHandle;
                }
                else
                {
                    pollingHandle.Dispose();
                }
            }

            SafeLog($"[DataSubscription] Registered polling subscription. id={subscriptionId}, key={key}, interval={SubscriptionPollIntervalMs}ms");
            return new SubscriptionHandle(this, subscriptionId);
        }

        internal Task RefreshSubscriptionAsync(long subscriptionId, CancellationToken ct = default)
        {
            PollingSubscriptionEntry? entry;
            lock (_subscriptionRegistryGate)
            {
                _subscriptionRegistry.TryGetValue(subscriptionId, out entry);
            }

            if (entry == null)
                throw new SubscriptionNotFoundException(subscriptionId, $"Subscription {subscriptionId} is not registered.");

            return RefreshEntryAsync(entry, ct);
        }

        internal void UnregisterSubscription(long subscriptionId)
        {
            IDisposable? polling = null;
            lock (_subscriptionRegistryGate)
            {
                if (_subscriptionRegistry.TryGetValue(subscriptionId, out var entry))
                {
                    polling = entry.PollingHandle;
                    _subscriptionRegistry.Remove(subscriptionId);
                }
            }

            polling?.Dispose();
            SafeLog($"[DataSubscription] Unregistered polling subscription. id={subscriptionId}");
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
            return RefreshSubscriptionAsync(subscriptionId);
        }

        public Task<ISharedEntity<TDto>> SelectEntity<TEntity, TDto>(string playerId, Func<TEntity, TDto> map)
            where TEntity : class
            where TDto : class, new()
        {
            if (string.IsNullOrWhiteSpace(playerId))
                throw new ArgumentException("Player id is required.", nameof(playerId));

            if (map == null)
                throw new ArgumentNullException(nameof(map));

            var entityTypeName = typeof(TEntity).Name;
            var query = QueryBuilder.BuildQuery<TEntity>(entityTypeName, playerId);
            var variables = QueryBuilder.BuildVariables(playerId);

            SafeLog($"[DataSubscription] Opening polling subscription. entity={entityTypeName}, key={playerId}, query={query}");

            var subscriptionId = NextSubscriptionId();
            var sharedEntity = new SharedEntity<TDto>(
                this,
                subscriptionId,
                playerId,
                entityTypeName,
                null,
                query,
                variables,
                raw =>
                {
                    if (raw == null)
                        return new TDto();

                    var json = raw is string str ? str : JsonConvert.SerializeObject(raw);
                    var entity = JsonConvert.DeserializeObject<TEntity>(json);
                    return entity == null ? new TDto() : map(entity);
                });

            return Task.FromResult<ISharedEntity<TDto>>(sharedEntity);
        }

        public async Task<DataGetResponse> GetDataByKeyAsync(
            string key,
            string query,
            Dictionary<string, object> variables,
            CancellationToken ct = default)
        {
            var request = CreateDataGetRequest(key, query, variables);
            return await SendDataGetRequestAsync(request, DataGetResponseTimeoutMs, ct);
        }

        public IDisposable StartDataByKeyPolling(
            string key,
            string query,
            Dictionary<string, object> variables,
            Action<DataGetResponse> onData,
            Action<Exception>? onError = null)
        {
            return StartDataByKeyPollingInternal(
                key,
                query,
                variables,
                DefaultDataGetPollIntervalMs,
                DefaultDataGetPollRequestTimeoutMs,
                onData,
                onError);
        }

        private IDisposable StartDataByKeyPollingInternal(
            string key,
            string query,
            Dictionary<string, object> variables,
            int intervalMs,
            int requestTimeoutMs,
            Action<DataGetResponse> onData,
            Action<Exception>? onError = null)
        {
            if (onData == null)
                throw new ArgumentNullException(nameof(onData));

            var cts = new CancellationTokenSource();
            var variablesSnapshot = CloneVariables(variables);

            var pollingTask = PollDataByKeyLoopAsync(
                key,
                query,
                variablesSnapshot,
                intervalMs,
                requestTimeoutMs,
                onData,
                onError,
                cts.Token);

            return new PollingHandle(cts, pollingTask);
        }

        private async Task<DataGetResponse> SendDataGetRequestAsync(
            DataGetRequest request,
            int timeoutMs,
            CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<DataGetResponse>();
            IDisposable? responseSubscription = null;
            IDisposable? responseByCommandSubscription = null;
            IDisposable? responseByModuleCommandSubscription = null;
            IDisposable? commandErrorSubscription = null;

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

            try
            {
                await _transport.SendAsync(request, "module_dataflow");
                var effectiveTimeoutMs = timeoutMs > 0 ? timeoutMs : DataGetResponseTimeoutMs;
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
            }
        }

        private async Task PollDataByKeyLoopAsync(
            string key,
            string query,
            Dictionary<string, object> variables,
            int intervalMs,
            int requestTimeoutMs,
            Action<DataGetResponse> onData,
            Action<Exception>? onError,
            CancellationToken ct)
        {
            SafeLog($"[DataGet] Polling started. interval={intervalMs}ms, key={key}");
            PlayServState? pausedAtState = null;

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var sdkState = _transport.State;
                    if (sdkState != PlayServState.Online)
                    {
                        if (pausedAtState != sdkState)
                        {
                            SafeLogWarning($"[DataGet] poll paused. sdkState={sdkState}");
                            pausedAtState = sdkState;
                        }

                        try
                        {
                            await DelayWithCancellationAsync(intervalMs, ct);
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested)
                        {
                            break;
                        }

                        continue;
                    }

                    if (pausedAtState.HasValue)
                    {
                        SafeLog($"[DataGet] poll resumed. sdkState={sdkState}");
                        pausedAtState = null;
                    }

                    var startedAt = DateTime.UtcNow;
                    try
                    {
                        var request = CreateDataGetRequest(key, query, variables);
#if PlayServ_Logs
                        SafeLog($"[DataGet] -> poll request send. requestId={request.RequestId}, key={key}, timeout={requestTimeoutMs}ms");
#endif
                        var response = await SendDataGetRequestAsync(request, requestTimeoutMs, ct);
#if PlayServ_Logs
                        SafeLog($"[DataGet] <- poll response received. requestId={request.RequestId}, hasError={(response?.HasError ?? false)}");
#endif
                        onData(response);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        SafeLogError($"[DataGet] Poll request failed: {ex.Message}");
                        SafeInvokeOnError(onError, ex);
                    }

                    var elapsedMs = (int)(DateTime.UtcNow - startedAt).TotalMilliseconds;
                    var delayMs = Math.Max(0, intervalMs - elapsedMs);
                    try
                    {
                        await DelayWithCancellationAsync(delayMs, ct);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                SafeLogError($"[DataGet] Polling loop crashed: {ex.Message}");
                SafeInvokeOnError(onError, ex);
            }
            finally
            {
                SafeLog($"[DataGet] Polling stopped. key={key}");
            }
        }

        private async Task RefreshEntryAsync(PollingSubscriptionEntry entry, CancellationToken ct)
        {
            try
            {
                var request = CreateDataGetRequest(entry.Key, entry.Query, entry.Variables);
                var response = await SendDataGetRequestAsync(request, SubscriptionPollRequestTimeoutMs, ct);
                ProcessSubscriptionResponse(entry.SubscriptionId, response);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                ProcessSubscriptionException(entry.SubscriptionId, ex);
            }
        }

        private void ProcessSubscriptionException(long subscriptionId, Exception ex)
        {
            var wrapped = ex as DataSubscriptionException ??
                          new DataSubscriptionException(0, ex?.Message ?? "Unknown subscription polling error.");
            RaiseSubscriptionError(subscriptionId, wrapped);
        }

        private void ProcessSubscriptionResponse(long subscriptionId, DataGetResponse response)
        {
            if (response == null)
            {
                RaiseSubscriptionError(subscriptionId, new DataSubscriptionException(0, "DataGet response is null."));
                return;
            }

            if (response.HasError)
            {
                RaiseSubscriptionError(subscriptionId, MapDataGetError(response.Error));
                return;
            }

            PollingSubscriptionEntry? entry;
            lock (_subscriptionRegistryGate)
            {
                _subscriptionRegistry.TryGetValue(subscriptionId, out entry);
            }

            if (entry == null)
                return;

            var payloadToken = ExtractSubscriptionPayload(response.Result?.Data, entry.RootFieldName);
            var payloadFingerprint = payloadToken == null ? string.Empty : payloadToken.ToString(Formatting.None);

            var shouldNotify = false;
            Action<object?>? onChanged = null;
            object? callbackPayload = null;

            lock (_subscriptionRegistryGate)
            {
                if (_subscriptionRegistry.TryGetValue(subscriptionId, out var current))
                {
                    if (!current.HasSnapshot || !string.Equals(current.LastSnapshotJson, payloadFingerprint, StringComparison.Ordinal))
                    {
                        current.HasSnapshot = true;
                        current.LastSnapshotJson = payloadFingerprint;
                        shouldNotify = true;
                        onChanged = current.OnChanged;
                        callbackPayload = payloadToken?.DeepClone();
                    }
                }
            }

            if (!shouldNotify || onChanged == null)
                return;

            try
            {
                onChanged(callbackPayload);
            }
            catch (Exception ex)
            {
                SafeLogError($"[DataSubscription] Changed callback failed. id={subscriptionId}, error={ex.Message}");
            }
        }

        private void RaiseSubscriptionError(long subscriptionId, DataSubscriptionException exception)
        {
            Action<DataSubscriptionException>? onError = null;
            lock (_subscriptionRegistryGate)
            {
                if (_subscriptionRegistry.TryGetValue(subscriptionId, out var entry))
                    onError = entry.OnError;
            }

            if (onError == null)
                return;

            try
            {
                onError(exception);
            }
            catch (Exception ex)
            {
                SafeLogError($"[DataSubscription] Error callback failed. id={subscriptionId}, error={ex.Message}");
            }
        }

        private static DataSubscriptionException MapDataGetError(DataGetError? error)
        {
            if (error == null)
                return new DataSubscriptionException(0, "Unknown data get error.");

            var code = error.Code;
            var message = error.Message ?? "Unknown data get error.";

            return code switch
            {
                31002 => new TargetNotFoundException(message),
                SubscriptionNotFoundException.Code => new SubscriptionNotFoundException(0, message),
                31001 => new AccessDeniedException(message),
                _ => new DataSubscriptionException(code, message)
            };
        }

        private static JToken? ExtractSubscriptionPayload(JToken? data, string rootFieldName)
        {
            if (data == null)
                return null;

            if (data is not JObject obj)
                return data;

            if (!string.IsNullOrWhiteSpace(rootFieldName))
            {
                if (obj.TryGetValue(rootFieldName, StringComparison.Ordinal, out var exact))
                    return exact;

                if (obj.TryGetValue(rootFieldName, StringComparison.OrdinalIgnoreCase, out var insensitive))
                    return insensitive;
            }

            foreach (var property in obj.Properties())
            {
                return property.Value;
            }

            return data;
        }

        private static async Task DelayWithCancellationAsync(int delayMs, CancellationToken ct)
        {
            if (delayMs <= 0)
                return;

#if UNITY_WEBGL && !UNITY_EDITOR
            var stopwatch = Stopwatch.StartNew();
            while (!ct.IsCancellationRequested)
            {
                if (stopwatch.ElapsedMilliseconds >= delayMs)
                    break;

                await Task.Yield();
            }

            if (ct.IsCancellationRequested)
                throw new OperationCanceledException(ct);
#else
            await Task.Delay(delayMs, ct);
#endif
        }

        private static async Task<bool> WaitForCompletionOrTimeoutAsync(Task task, int timeoutMs, CancellationToken ct)
        {
            if (task.IsCompleted)
                return true;

            if (timeoutMs <= 0)
                timeoutMs = 1;

#if UNITY_WEBGL && !UNITY_EDITOR
            var stopwatch = Stopwatch.StartNew();
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

        private static bool TryMapDataGetResponse(object command, out DataGetResponse response)
        {
            response = null!;
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

        private static Dictionary<string, object> CloneVariables(Dictionary<string, object> variables)
        {
            if (variables == null || variables.Count == 0)
                return new Dictionary<string, object>();

            return new Dictionary<string, object>(variables);
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

        private static void SafeInvokeOnError(Action<Exception>? onError, Exception ex)
        {
            if (onError == null)
                return;

            try
            {
                onError(ex);
            }
            catch
            {
            }
        }

        public void Dispose()
        {
            List<IDisposable> handles = new List<IDisposable>();
            lock (_subscriptionRegistryGate)
            {
                foreach (var entry in _subscriptionRegistry.Values)
                {
                    if (entry.PollingHandle != null)
                        handles.Add(entry.PollingHandle);
                }

                _subscriptionRegistry.Clear();
            }

            foreach (var handle in handles)
            {
                handle.Dispose();
            }
        }

        private sealed class PollingSubscriptionEntry
        {
            public PollingSubscriptionEntry(
                long subscriptionId,
                string key,
                string query,
                Dictionary<string, object> variables,
                string rootFieldName,
                Action<object?> onChanged,
                Action<DataSubscriptionException>? onError)
            {
                SubscriptionId = subscriptionId;
                Key = key;
                Query = query;
                Variables = variables;
                RootFieldName = rootFieldName;
                OnChanged = onChanged;
                OnError = onError;
            }

            public long SubscriptionId { get; }
            public string Key { get; }
            public string Query { get; }
            public Dictionary<string, object> Variables { get; }
            public string RootFieldName { get; }
            public Action<object?> OnChanged { get; }
            public Action<DataSubscriptionException>? OnError { get; }
            public bool HasSnapshot { get; set; }
            public string LastSnapshotJson { get; set; } = string.Empty;
            public IDisposable? PollingHandle { get; set; }
        }

        private sealed class PollingHandle : IDisposable
        {
            private CancellationTokenSource? _cts;
            private Task? _pollingTask;

            public PollingHandle(CancellationTokenSource cts, Task pollingTask)
            {
                _cts = cts;
                _pollingTask = pollingTask;
            }

            public void Dispose()
            {
                var cts = Interlocked.Exchange(ref _cts, null);
                if (cts == null)
                    return;

                if (!cts.IsCancellationRequested)
                    cts.Cancel();

                cts.Dispose();
                _pollingTask = null;
            }
        }

        private sealed class SubscriptionHandle : IDisposable
        {
            private PlayServDataSubscriptionAdapter? _adapter;
            private readonly long _subscriptionId;

            public SubscriptionHandle(PlayServDataSubscriptionAdapter adapter, long subscriptionId)
            {
                _adapter = adapter;
                _subscriptionId = subscriptionId;
            }

            public void Dispose()
            {
                var adapter = Interlocked.Exchange(ref _adapter, null);
                if (adapter == null)
                    return;

                adapter.UnregisterSubscription(_subscriptionId);
            }
        }
    }
}
