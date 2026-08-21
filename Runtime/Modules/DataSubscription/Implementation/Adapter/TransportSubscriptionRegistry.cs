using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Playserv.DataSubscription.Exceptions;
using Playserv.DataSubscription.Responses;
using Playserv.Serialization;
using Playserv.Wrapper;
using ILogger = Playserv.Proxy.Logging.ILogger;

namespace Playserv.DataSubscription
{
    internal sealed class TransportSubscriptionRegistry : IDisposable
    {
        private readonly object _gate = new object();
        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly Dictionary<long, Entry> _entriesByServerId = new Dictionary<long, Entry>();
        private readonly Dictionary<long, DataSubscriptionUpdate> _orphanUpdates = new Dictionary<long, DataSubscriptionUpdate>();
        private readonly HashSet<long> _restoredReferencesReleased = new HashSet<long>();
        private readonly TransportSubscriptionClient _openClient;
        private readonly DataSubscriptionCloseClient _closeClient;
        private readonly IJsonCodec _jsonCodec;
        private readonly ILogger _logger;
        private readonly IDisposable _updates;
        private bool _disposed;

        public TransportSubscriptionRegistry(
            Playserv.Modules.IPlayServCommandBus commandBus,
            TransportSubscriptionClient openClient,
            DataSubscriptionCloseClient closeClient,
            IJsonCodec jsonCodec,
            ILogger logger)
        {
            _openClient = openClient ?? throw new ArgumentNullException(nameof(openClient));
            _closeClient = closeClient ?? throw new ArgumentNullException(nameof(closeClient));
            _jsonCodec = jsonCodec ?? throw new ArgumentNullException(nameof(jsonCodec));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            if (commandBus == null)
                throw new ArgumentNullException(nameof(commandBus));
            _updates = commandBus.On<DataSubscriptionUpdate>(ProcessUpdate);
        }

        public async Task<TransportSubscriptionLease> AcquireAsync(
            string query,
            Dictionary<string, object> variables,
            string rootFieldName,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(query))
                throw new ArgumentException("Subscription query is required.", nameof(query));
            ct.ThrowIfCancellationRequested();

            var normalizedVariables = DataSubscriptionRequestSupport.CloneVariables(variables);
            var key = BuildKey(query, normalizedVariables);
            Entry entry;
            Task<long> openTask;
            lock (_gate)
            {
                ThrowIfDisposed();
                if (!_entries.TryGetValue(key, out entry))
                {
                    entry = new Entry(key, query.Trim(), normalizedVariables, rootFieldName ?? string.Empty);
                    _entries.Add(key, entry);
                    entry.Generation++;
                    entry.OpenTask = OpenAndBindAsync(entry, entry.Generation);
                }
                entry.PendingAcquires++;
                openTask = entry.OpenTask ?? Task.FromResult(entry.ServerId);
            }

            try
            {
                await AwaitWithCancellation(openTask, ct);
                ct.ThrowIfCancellationRequested();

                lock (_gate)
                {
                    if (_disposed || !_entries.TryGetValue(key, out var current) || !ReferenceEquals(current, entry))
                        throw new ObjectDisposedException(nameof(TransportSubscriptionRegistry));
                    if (entry.State == PlayServSubscriptionState.Terminated)
                        throw new SubscriptionTerminatedException(entry.ServerId, entry.TerminalError?.Message);

                    entry.PendingAcquires--;
                    var lease = new TransportSubscriptionLease(this, entry, entry.LastUpdate);
                    entry.Leases.Add(lease);
                    entry.State = PlayServSubscriptionState.Active;
                    UpdateLeaseStates(entry);
                    return lease;
                }
            }
            catch
            {
                CleanupFailedAcquire(entry);
                throw;
            }
        }

        public void OnConnected()
        {
            List<Task> replays = new List<Task>();
            lock (_gate)
            {
                if (_disposed)
                    return;
                _orphanUpdates.Clear();
                foreach (var entry in _entries.Values.ToArray())
                {
                    if (entry.Leases.Count == 0 || entry.State == PlayServSubscriptionState.Terminated)
                        continue;

                    if (entry.ServerId > 0)
                        _entriesByServerId.Remove(entry.ServerId);
                    entry.State = PlayServSubscriptionState.Reconnecting;
                    UpdateLeaseStates(entry);
                    entry.Generation++;
                    entry.OpenTask = OpenAndBindAsync(entry, entry.Generation);
                    replays.Add(ObserveReplayAsync(entry, entry.Generation, entry.OpenTask));
                }
            }

            if (replays.Count > 0)
                _ = Task.WhenAll(replays);
        }

        public void DisposeLease(TransportSubscriptionLease lease)
        {
            _ = ReleaseLeaseAsync(lease, CancellationToken.None, fireAndForget: true);
        }

        public Task<PlayServSubscriptionCloseResult> CloseLeaseAsync(
            TransportSubscriptionLease lease,
            CancellationToken ct)
        {
            return ReleaseLeaseAsync(lease, ct, fireAndForget: false);
        }

        private async Task<PlayServSubscriptionCloseResult> ReleaseLeaseAsync(
            TransportSubscriptionLease lease,
            CancellationToken ct,
            bool fireAndForget)
        {
            if (lease == null)
                return PlayServSubscriptionCloseResult.Success(true);
            ct.ThrowIfCancellationRequested();

            long serverId = 0;
            var alreadyClosed = false;
            lock (_gate)
            {
                if (!lease.TryRelease())
                {
                    alreadyClosed = true;
                }
                else
                {
                    var entry = lease.Entry;
                    entry.Leases.Remove(lease);
                    if (entry.State == PlayServSubscriptionState.Terminated)
                    {
                        alreadyClosed = true;
                    }
                    else
                    {
                        lease.SetState(PlayServSubscriptionState.Closed);
                    }
                    if (entry.Leases.Count == 0)
                    {
                        _entries.Remove(entry.Key);
                        entry.Generation++;
                        serverId = entry.State == PlayServSubscriptionState.Terminated ? 0 : entry.ServerId;
                        if (serverId > 0)
                            _entriesByServerId.Remove(serverId);
                    }
                }
            }

            if (alreadyClosed || serverId <= 0)
                return PlayServSubscriptionCloseResult.Success(alreadyClosed);

            var result = await _closeClient.CloseAsync(
                serverId,
                PlayServDataSubscriptionAdapter.DataSubscriptionCloseTimeoutMs,
                ct);
            if (fireAndForget && !result.IsSuccess)
                SafeLogWarning($"[DataSubscription] Best-effort close failed. id={serverId}, code={result.Error?.Code}");
            return result;
        }

        private async Task<long> OpenAndBindAsync(Entry entry, int generation)
        {
            var serverId = await _openClient.TryOpenTransportSubscriptionAsync(
                entry.Query,
                entry.Variables,
                allowFallbackToPolling: false,
                CancellationToken.None);
            if (!serverId.HasValue || serverId.Value <= 0)
                throw new DataSubscriptionException(0, "Transport subscription returned no server ID.");

            DataSubscriptionUpdate buffered = null;
            var stale = false;
            lock (_gate)
            {
                stale = _disposed ||
                        generation != entry.Generation ||
                        !_entries.TryGetValue(entry.Key, out var current) ||
                        !ReferenceEquals(current, entry);
                if (!stale)
                {
                    entry.ServerId = serverId.Value;
                    entry.State = PlayServSubscriptionState.Active;
                    _entriesByServerId[serverId.Value] = entry;
                    _orphanUpdates.TryGetValue(serverId.Value, out buffered);
                    _orphanUpdates.Remove(serverId.Value);
                    UpdateLeaseStates(entry);
                }
            }

            if (stale)
            {
                await _closeClient.CloseAsync(
                    serverId.Value,
                    PlayServDataSubscriptionAdapter.DataSubscriptionCloseTimeoutMs,
                    CancellationToken.None,
                    allowDuringReconnect: true);
            }
            else if (buffered != null)
            {
                ProcessBoundUpdate(entry, buffered);
            }
            return serverId.Value;
        }

        private async Task ObserveReplayAsync(Entry entry, int generation, Task<long> replay)
        {
            try
            {
                await replay;
            }
            catch (Exception ex)
            {
                var failure = ex as DataSubscriptionException ??
                              new DataSubscriptionException(0, ex.Message);
                List<TransportSubscriptionLease> leases = null;
                var terminal = !failure.UnifiedError.Retryable;
                lock (_gate)
                {
                    if (_disposed || generation != entry.Generation ||
                        !_entries.TryGetValue(entry.Key, out var current) ||
                        !ReferenceEquals(current, entry))
                    {
                        return;
                    }

                    leases = entry.Leases.ToList();
                    if (terminal)
                    {
                        entry.State = PlayServSubscriptionState.Terminated;
                        entry.TerminalError = failure.UnifiedError;
                        _entries.Remove(entry.Key);
                    }
                    UpdateLeaseStates(entry);
                }

                foreach (var lease in leases)
                {
                    if (terminal)
                        lease.NotifyTerminated(failure);
                    else
                        lease.NotifyFailure(failure);
                }
            }
        }

        private void ProcessUpdate(DataSubscriptionUpdate update)
        {
            if (update == null || update.DataSubscriptionId <= 0)
                return;

            if (IsRestoredOverwrite(update))
            {
                var shouldRelease = false;
                lock (_gate)
                    shouldRelease = !_disposed && _restoredReferencesReleased.Add(update.DataSubscriptionId);
                if (shouldRelease)
                    _ = ReleaseRestoredReferenceAsync(update.DataSubscriptionId);
                return;
            }

            Entry entry;
            lock (_gate)
            {
                if (_disposed)
                    return;
                if (!_entriesByServerId.TryGetValue(update.DataSubscriptionId, out entry))
                {
                    if (update.HasError)
                        return;
                    if (_orphanUpdates.Count >= 128)
                        _orphanUpdates.Remove(_orphanUpdates.Keys.First());
                    _orphanUpdates[update.DataSubscriptionId] = CloneUpdate(update);
                    return;
                }
            }
            ProcessBoundUpdate(entry, update);
        }

        private void ProcessBoundUpdate(Entry entry, DataSubscriptionUpdate update)
        {
            var errorCode = update.ErrorCode ?? 0;
            if (update.HasError &&
                (errorCode == SubscriptionTerminatedException.Code ||
                 errorCode == 31002 ||
                 string.Equals(update.UpdateType, "Terminated", StringComparison.OrdinalIgnoreCase)))
            {
                var failure = errorCode == SubscriptionTerminatedException.Code
                    ? (DataSubscriptionException)new SubscriptionTerminatedException(entry.ServerId, update.EffectiveErrorMessage)
                    : errorCode == 31002
                        ? new TargetNotFoundException(update.EffectiveErrorMessage ?? "Subscription target was deleted.")
                        : DataSubscriptionErrorMapper.MapErrorToException(new DataSubscriptionError
                        {
                            ErrorCode = errorCode,
                            Message = update.EffectiveErrorMessage
                        });
                TerminateEntry(entry, failure);
                return;
            }

            List<TransportSubscriptionLease> leases;
            var snapshot = CloneUpdate(update);
            lock (_gate)
            {
                if (_disposed || !_entries.TryGetValue(entry.Key, out var current) || !ReferenceEquals(current, entry))
                    return;
                entry.LastUpdate = snapshot;
                leases = entry.Leases.ToList();
            }
            foreach (var lease in leases)
                lease.NotifyUpdate(CloneUpdate(snapshot));
        }

        private void TerminateEntry(Entry entry, DataSubscriptionException failure)
        {
            List<TransportSubscriptionLease> leases;
            lock (_gate)
            {
                if (_disposed || !_entries.TryGetValue(entry.Key, out var current) || !ReferenceEquals(current, entry))
                    return;
                _entries.Remove(entry.Key);
                if (entry.ServerId > 0)
                    _entriesByServerId.Remove(entry.ServerId);
                entry.Generation++;
                entry.State = PlayServSubscriptionState.Terminated;
                entry.TerminalError = failure.UnifiedError;
                leases = entry.Leases.ToList();
                UpdateLeaseStates(entry);
            }
            foreach (var lease in leases)
                lease.NotifyTerminated(failure);
        }

        private async Task ReleaseRestoredReferenceAsync(long serverId)
        {
            var result = await _closeClient.CloseAsync(
                serverId,
                PlayServDataSubscriptionAdapter.DataSubscriptionCloseTimeoutMs,
                CancellationToken.None,
                allowDuringReconnect: true);
            if (!result.IsSuccess)
                SafeLogWarning($"[DataSubscription] Failed to normalize restored reference. id={serverId}, code={result.Error?.Code}");
        }

        private void CleanupFailedAcquire(Entry entry)
        {
            lock (_gate)
            {
                if (entry.PendingAcquires > 0)
                    entry.PendingAcquires--;
                if (entry.PendingAcquires != 0 || entry.Leases.Count != 0)
                    return;
                if (_entries.TryGetValue(entry.Key, out var current) && ReferenceEquals(current, entry))
                {
                    _entries.Remove(entry.Key);
                    entry.Generation++;
                    if (entry.ServerId > 0)
                        _entriesByServerId.Remove(entry.ServerId);
                }
            }
        }

        private string BuildKey(string query, Dictionary<string, object> variables)
        {
            var plain = _jsonCodec.ToPlainValue(variables ?? new Dictionary<string, object>());
            return query.Trim() + "\n" + _jsonCodec.ToCanonicalJson(Canonicalize(plain));
        }

        private static object Canonicalize(object value)
        {
            if (value == null || value is string)
                return value;
            if (value is IDictionary dictionary)
            {
                var result = new SortedDictionary<string, object>(StringComparer.Ordinal);
                foreach (DictionaryEntry pair in dictionary)
                    result[Convert.ToString(pair.Key)] = Canonicalize(pair.Value);
                return result;
            }
            if (value is IEnumerable enumerable)
            {
                var result = new List<object>();
                foreach (var item in enumerable)
                    result.Add(Canonicalize(item));
                return result;
            }
            return value;
        }

        private DataSubscriptionUpdate CloneUpdate(DataSubscriptionUpdate update)
        {
            if (update == null)
                return null;
            return new DataSubscriptionUpdate
            {
                RequestId = update.RequestId,
                DataSubscriptionId = update.DataSubscriptionId,
                UpdateType = update.UpdateType,
                IsCollection = update.IsCollection,
                Data = _jsonCodec.Clone(update.Data),
                ErrorCode = update.ErrorCode,
                ErrorMessage = update.ErrorMessage,
                Message = update.Message,
                EntityType = update.EntityType,
                IdValue = update.IdValue
            };
        }

        private static bool IsRestoredOverwrite(DataSubscriptionUpdate update) =>
            update.RequestId == 0 && update.IsOverwrite && !string.IsNullOrWhiteSpace(update.EntityType);

        private static async Task AwaitWithCancellation(Task task, CancellationToken ct)
        {
            if (task.IsCompleted)
            {
                await task;
                return;
            }
            var canceled = new TaskCompletionSource<bool>();
            using (ct.Register(() => canceled.TrySetResult(true)))
            {
                if (await Task.WhenAny(task, canceled.Task) != task)
                    throw new OperationCanceledException(ct);
            }
            await task;
        }

        private static void UpdateLeaseStates(Entry entry)
        {
            foreach (var lease in entry.Leases)
                lease.SetState(entry.State, entry.TerminalError);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(TransportSubscriptionRegistry));
        }

        private void SafeLogWarning(string message)
        {
            try { _logger.LogWarning(message); } catch { }
        }

        public void Dispose()
        {
            List<TransportSubscriptionLease> leases;
            lock (_gate)
            {
                if (_disposed)
                    return;
                _disposed = true;
                leases = _entries.Values.SelectMany(entry => entry.Leases).ToList();
                _entries.Clear();
                _entriesByServerId.Clear();
                _orphanUpdates.Clear();
            }
            _updates?.Dispose();
            foreach (var lease in leases)
                lease.CloseLocally();
        }

        internal sealed class Entry
        {
            public Entry(
                string key,
                string query,
                Dictionary<string, object> variables,
                string rootFieldName)
            {
                Key = key;
                Query = query;
                Variables = variables;
                RootFieldName = rootFieldName;
            }

            public string Key { get; }
            public string Query { get; }
            public Dictionary<string, object> Variables { get; }
            public string RootFieldName { get; }
            public List<TransportSubscriptionLease> Leases { get; } = new List<TransportSubscriptionLease>();
            public long ServerId;
            public int Generation;
            public int PendingAcquires;
            public Task<long> OpenTask;
            public DataSubscriptionUpdate LastUpdate;
            public PlayServSubscriptionState State = PlayServSubscriptionState.Reconnecting;
            public PlayServError TerminalError;
        }
    }

    internal sealed class TransportSubscriptionLease
    {
        private readonly TransportSubscriptionRegistry _registry;
        private int _released;

        public TransportSubscriptionLease(
            TransportSubscriptionRegistry registry,
            TransportSubscriptionRegistry.Entry entry,
            DataSubscriptionUpdate initialUpdate)
        {
            _registry = registry;
            Entry = entry;
            InitialUpdate = initialUpdate;
            State = entry.State;
            TerminalError = entry.TerminalError;
        }

        internal TransportSubscriptionRegistry.Entry Entry { get; }
        internal DataSubscriptionUpdate InitialUpdate { get; }
        internal long SubscriptionId => Entry.ServerId;
        internal PlayServSubscriptionState State { get; private set; }
        internal PlayServError TerminalError { get; private set; }
        internal event Action<DataSubscriptionUpdate> Updated;
        internal event Action<DataSubscriptionException> Failed;
        internal event Action<DataSubscriptionException> Terminated;

        internal Task<PlayServSubscriptionCloseResult> CloseAsync(CancellationToken ct) =>
            _registry.CloseLeaseAsync(this, ct);

        internal void Dispose() => _registry.DisposeLease(this);

        internal bool TryRelease() => Interlocked.Exchange(ref _released, 1) == 0;

        internal void SetState(PlayServSubscriptionState state, PlayServError terminalError = null)
        {
            State = state;
            if (terminalError != null)
                TerminalError = terminalError;
        }

        internal void NotifyUpdate(DataSubscriptionUpdate update)
        {
            if (Volatile.Read(ref _released) == 0)
                Updated?.Invoke(update);
        }

        internal void NotifyFailure(DataSubscriptionException failure)
        {
            if (Volatile.Read(ref _released) == 0)
                Failed?.Invoke(failure);
        }

        internal void NotifyTerminated(DataSubscriptionException failure)
        {
            SetState(PlayServSubscriptionState.Terminated, failure.UnifiedError);
            if (Volatile.Read(ref _released) == 0)
                Terminated?.Invoke(failure);
        }

        internal void CloseLocally()
        {
            Interlocked.Exchange(ref _released, 1);
            SetState(PlayServSubscriptionState.Closed);
        }
    }
}
