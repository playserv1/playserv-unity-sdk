using System;
using System.Collections.Generic;
using System.Threading;
using Playserv.DataSubscription.Exceptions;
using Playserv.DataSubscription.Responses;
using Playserv.Serialization;
using ILogger = Playserv.Proxy.Logging.ILogger;

namespace Playserv.DataSubscription
{
    internal sealed class DataSubscriptionRegistry
    {
        private readonly ILogger _logger;
        private readonly IJsonCodec _jsonCodec;
        private readonly object _gate = new object();
        private readonly Dictionary<long, DataSubscriptionPollingEntry> _entries = new Dictionary<long, DataSubscriptionPollingEntry>();
        private long _subscriptionIdCounter;

        public DataSubscriptionRegistry(ILogger logger, IJsonCodec jsonCodec)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _jsonCodec = jsonCodec ?? throw new ArgumentNullException(nameof(jsonCodec));
        }

        public long NextSubscriptionId()
        {
            return Interlocked.Increment(ref _subscriptionIdCounter);
        }

        public DataSubscriptionPollingEntry RegisterPollingSubscription(
            long subscriptionId,
            string key,
            string query,
            Dictionary<string, object> variables,
            string rootFieldName,
            Action<object> onChanged,
            Action<DataSubscriptionException> onError)
        {
            if (subscriptionId <= 0)
                throw new ArgumentOutOfRangeException(nameof(subscriptionId));

            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Key is required.", nameof(key));

            if (string.IsNullOrWhiteSpace(query))
                throw new ArgumentException("Query is required.", nameof(query));

            if (onChanged == null)
                throw new ArgumentNullException(nameof(onChanged));

            var entry = new DataSubscriptionPollingEntry(
                subscriptionId,
                key,
                query,
                CloneVariables(variables),
                rootFieldName ?? string.Empty,
                onChanged,
                onError);

            lock (_gate)
            {
                if (_entries.ContainsKey(subscriptionId))
                    throw new InvalidOperationException($"Subscription with id={subscriptionId} is already registered.");

                _entries[subscriptionId] = entry;
            }

            return entry;
        }

        public void AttachPollingHandle(long subscriptionId, IDisposable pollingHandle)
        {
            lock (_gate)
            {
                if (_entries.TryGetValue(subscriptionId, out var entry))
                {
                    entry.PollingHandle = pollingHandle;
                    return;
                }
            }

            pollingHandle?.Dispose();
        }

        public IDisposable CreateSubscriptionHandle(long subscriptionId, Action<long> unregister)
        {
            return new SubscriptionHandle(subscriptionId, unregister);
        }

        public bool TryGetEntry(long subscriptionId, out DataSubscriptionPollingEntry entry)
        {
            lock (_gate)
            {
                return _entries.TryGetValue(subscriptionId, out entry);
            }
        }

        public DataSubscriptionPollingEntry GetRequiredEntry(long subscriptionId)
        {
            if (TryGetEntry(subscriptionId, out var entry))
                return entry;

            throw new SubscriptionNotFoundException(subscriptionId, $"Subscription {subscriptionId} is not registered.");
        }

        public void UnregisterSubscription(long subscriptionId)
        {
            IDisposable polling = null;
            lock (_gate)
            {
                if (_entries.TryGetValue(subscriptionId, out var entry))
                {
                    polling = entry.PollingHandle;
                    _entries.Remove(subscriptionId);
                }
            }

            polling?.Dispose();
            SafeLog($"[DataSubscription] Unregistered polling subscription. id={subscriptionId}");
        }

        public void ProcessException(long subscriptionId, Exception ex)
        {
            var wrapped = ex as DataSubscriptionException ??
                          new DataSubscriptionException(0, ex?.Message ?? "Unknown subscription polling error.");
            RaiseSubscriptionError(subscriptionId, wrapped);
        }

        public void ProcessResponse(long subscriptionId, DataGetResponse response)
        {
            if (response == null)
            {
                RaiseSubscriptionError(subscriptionId, new DataSubscriptionException(0, "DataGet response is null."));
                return;
            }

            if (response.HasError)
            {
                RaiseSubscriptionError(subscriptionId, DataSubscriptionErrorMapper.MapDataGetError(response.Error));
                return;
            }

            if (!TryGetEntry(subscriptionId, out var entry))
                return;

            var payloadToken = DataSubscriptionErrorMapper.ExtractSubscriptionPayload(response.Result?.Data, entry.RootFieldName, _jsonCodec);
            var payloadFingerprint = payloadToken == null ? string.Empty : _jsonCodec.ToCanonicalJson(payloadToken);

            var shouldNotify = false;
            Action<object> onChanged = null;
            object callbackPayload = null;

            lock (_gate)
            {
                if (_entries.TryGetValue(subscriptionId, out var current))
                {
                    if (!current.HasSnapshot || !string.Equals(current.LastSnapshotJson, payloadFingerprint, StringComparison.Ordinal))
                    {
                        current.HasSnapshot = true;
                        current.LastSnapshotJson = payloadFingerprint;
                        shouldNotify = true;
                        onChanged = current.OnChanged;
                        callbackPayload = _jsonCodec.Clone(payloadToken);
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

        public void DisposeAll()
        {
            List<IDisposable> handles = new List<IDisposable>();

            lock (_gate)
            {
                foreach (var entry in _entries.Values)
                {
                    if (entry.PollingHandle != null)
                        handles.Add(entry.PollingHandle);
                }

                _entries.Clear();
            }

            foreach (var handle in handles)
            {
                handle.Dispose();
            }
        }

        private void RaiseSubscriptionError(long subscriptionId, DataSubscriptionException exception)
        {
            Action<DataSubscriptionException> onError = null;

            lock (_gate)
            {
                if (_entries.TryGetValue(subscriptionId, out var entry))
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

        private static Dictionary<string, object> CloneVariables(Dictionary<string, object> variables)
        {
            if (variables == null || variables.Count == 0)
                return new Dictionary<string, object>();

            return new Dictionary<string, object>(variables);
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

        private sealed class SubscriptionHandle : IDisposable
        {
            private Action<long> _unregister;
            private readonly long _subscriptionId;

            public SubscriptionHandle(long subscriptionId, Action<long> unregister)
            {
                _subscriptionId = subscriptionId;
                _unregister = unregister ?? throw new ArgumentNullException(nameof(unregister));
            }

            public void Dispose()
            {
                var unregister = Interlocked.Exchange(ref _unregister, null);
                if (unregister == null)
                    return;

                unregister(_subscriptionId);
            }
        }
    }

    internal sealed class DataSubscriptionPollingEntry
    {
        public DataSubscriptionPollingEntry(
            long subscriptionId,
            string key,
            string query,
            Dictionary<string, object> variables,
            string rootFieldName,
            Action<object> onChanged,
            Action<DataSubscriptionException> onError)
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
        public Action<object> OnChanged { get; }
        public Action<DataSubscriptionException> OnError { get; }
        public bool HasSnapshot { get; set; }
        public string LastSnapshotJson { get; set; } = string.Empty;
        public IDisposable PollingHandle { get; set; }
    }
}
