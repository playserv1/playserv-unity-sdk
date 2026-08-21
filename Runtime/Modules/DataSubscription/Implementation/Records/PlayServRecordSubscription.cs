using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Playserv.DataSubscription;
using Playserv.DataSubscription.Exceptions;
using Playserv.DataSubscription.Responses;
using Playserv.Proxy.Logging;
using Playserv.Wrapper;

namespace Playserv.Data
{
    /// <summary>Describes one canonical realtime refresh of a managed record handle.</summary>
    public sealed class PlayServRecordChange<T>
    {
        internal PlayServRecordChange(
            PlayServRecord<T> record,
            IReadOnlyList<string> changedFields,
            bool overwrotePendingChanges)
        {
            Record = record ?? throw new ArgumentNullException(nameof(record));
            ChangedFields = ReadOnly(changedFields);
            OverwrotePendingChanges = overwrotePendingChanges;
        }

        public PlayServRecord<T> Record { get; }

        /// <summary>Wire names of top-level fields changed since the previous canonical snapshot.</summary>
        public IReadOnlyList<string> ChangedFields { get; }

        /// <summary>True when backend-wins synchronization replaced unsaved local edits.</summary>
        public bool OverwrotePendingChanges { get; }

        private static IReadOnlyList<string> ReadOnly(IReadOnlyList<string> values) =>
            new ReadOnlyCollection<string>((values ?? Array.Empty<string>()).ToArray());
    }

    /// <summary>
    /// Captures a backend-wins reconciliation where a realtime refresh replaced unsaved local edits.
    /// </summary>
    public sealed class PlayServRecordRealtimeConflict<T>
    {
        internal PlayServRecordRealtimeConflict(
            PlayServRecord<T> record,
            T localValue,
            T remoteValue,
            IReadOnlyList<string> localChangedFields,
            IReadOnlyList<string> remoteChangedFields)
        {
            Record = record ?? throw new ArgumentNullException(nameof(record));
            LocalValue = localValue;
            RemoteValue = remoteValue;
            LocalChangedFields = ReadOnly(localChangedFields);
            RemoteChangedFields = ReadOnly(remoteChangedFields);
        }

        public PlayServRecord<T> Record { get; }

        /// <summary>Best-effort serialized clone of the value before backend-wins reconciliation.</summary>
        public T LocalValue { get; }

        /// <summary>The canonical remote value now stored by <see cref="Record"/>.</summary>
        public T RemoteValue { get; }

        public IReadOnlyList<string> LocalChangedFields { get; }

        public IReadOnlyList<string> RemoteChangedFields { get; }

        private static IReadOnlyList<string> ReadOnly(IReadOnlyList<string> values) =>
            new ReadOnlyCollection<string>((values ?? Array.Empty<string>()).ToArray());
    }

    /// <summary>
    /// Lifecycle and notifications for one realtime managed record handle. An explicit refresh
    /// completes after the record value, canonical snapshot, timestamps and ETag are synchronized.
    /// </summary>
    public interface IPlayServRecordSubscription<T> : IPlayServSubscriptionHandle, IPlayServRefreshableSubscription
    {
        PlayServRecord<T> Record { get; }

        event Action<PlayServRecordChange<T>> Changed;

        event Action<PlayServRecordRealtimeConflict<T>> Conflict;

        /// <summary>Legacy dataflow error surface retained alongside <see cref="IPlayServSubscriptionHandle.Failure"/>.</summary>
        event Action<DataSubscriptionException> Error;

        /// <summary>Raised when the REST metadata/ETag refresh fails while the subscription remains active.</summary>
        event Action<PlayServDataException> SynchronizationError;

        event Action Terminated;
    }

    internal sealed class PlayServRecordRealtimeApplyResult<T>
    {
        internal static readonly PlayServRecordRealtimeApplyResult<T> NotApplied =
            new PlayServRecordRealtimeApplyResult<T>(
                false,
                Array.Empty<string>(),
                Array.Empty<string>(),
                default,
                default);

        internal PlayServRecordRealtimeApplyResult(
            bool applied,
            IReadOnlyList<string> remoteChangedFields,
            IReadOnlyList<string> localChangedFields,
            T localValue,
            T remoteValue)
        {
            Applied = applied;
            RemoteChangedFields = remoteChangedFields ?? Array.Empty<string>();
            LocalChangedFields = localChangedFields ?? Array.Empty<string>();
            LocalValue = localValue;
            RemoteValue = remoteValue;
        }

        internal bool Applied { get; }
        internal IReadOnlyList<string> RemoteChangedFields { get; }
        internal IReadOnlyList<string> LocalChangedFields { get; }
        internal T LocalValue { get; }
        internal T RemoteValue { get; }
        internal bool HasConflict => LocalChangedFields.Count > 0;
    }

    internal sealed class PlayServRecordSubscription<T> : IPlayServRecordSubscription<T>
    {
        private readonly object _syncGate = new object();
        private readonly PlayServRecord<T> _record;
        private readonly PlayServDataSubscriptionAdapter _adapter;
        private readonly TransportSubscriptionLease _transportLease;
        private readonly SynchronizationContext _synchronizationContext;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly Dictionary<long, RefreshSynchronization> _refreshSynchronizations =
            new Dictionary<long, RefreshSynchronization>();
        private readonly ILogger _logger = PlayServLog.ForCategory(PlayServLogCategory.Data);
        private int _requestedVersion;
        private bool _workerRunning;
        private int _disposed;
        private int _terminalSignaled;
        private PlayServSubscriptionState? _localState;
        private PlayServError _localTerminalError;

        internal PlayServRecordSubscription(
            PlayServRecord<T> record,
            PlayServDataSubscriptionAdapter adapter,
            TransportSubscriptionLease transportLease)
        {
            _record = record ?? throw new ArgumentNullException(nameof(record));
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _transportLease = transportLease ?? throw new ArgumentNullException(nameof(transportLease));
            _synchronizationContext = SynchronizationContext.Current;
            _transportLease.Updated += OnServerUpdate;
            _transportLease.Failed += OnLeaseFailure;
            _transportLease.Terminated += OnLeaseTerminated;
        }

        public PlayServRecord<T> Record => _record;

        public PlayServSubscriptionState State =>
            _localState ??
            (Volatile.Read(ref _disposed) != 0
                ? PlayServSubscriptionState.Closed
                : _transportLease.State);

        public PlayServError TerminalError => _localTerminalError ?? _transportLease.TerminalError;

        public event Action<PlayServRecordChange<T>> Changed;
        public event Action<PlayServRecordRealtimeConflict<T>> Conflict;
        public event Action<DataSubscriptionException> Error;
        public event Action<PlayServDataException> SynchronizationError;
        public event Action<PlayServError> Failure;
        public event Action Terminated;

        internal async Task InitializeAsync(CancellationToken ct)
        {
            var initial = _transportLease.InitialUpdate;
            if (initial == null || initial.HasError || _record.MatchesRealtimeSnapshot(initial.Data))
                return;

            // SubscribeAsync returns only after the handle matches the initial server snapshot.
            // The caller has not yet received the handle, so this baseline refresh is silent.
            await SynchronizeOnceAsync(notify: false, ct);
        }

        private void OnServerUpdate(DataSubscriptionUpdate update)
        {
            if (Volatile.Read(ref _disposed) != 0 || update == null)
                return;

            if (update.HasError)
            {
                var failure = DataSubscriptionErrorMapper.MapErrorToException(
                    new DataSubscriptionError
                    {
                        ErrorCode = update.ErrorCode ?? 0,
                        Message = update.EffectiveErrorMessage
                    });
                if (update.ErrorCode == 49001 || update.ErrorCode == 31002 ||
                    string.Equals(update.UpdateType, "Terminated", StringComparison.OrdinalIgnoreCase))
                {
                    _ = TerminateAsync(failure, IsDeletion(failure), releaseLease: true);
                }
                else
                {
                    Dispatch(() => RaiseDataflowFailure(failure));
                }
                return;
            }

            QueueSynchronization(update.RequestId);
        }

        private void QueueSynchronization(long requestId = 0)
        {
            var startWorker = false;
            lock (_syncGate)
            {
                if (Volatile.Read(ref _disposed) != 0 || Volatile.Read(ref _terminalSignaled) != 0)
                    return;
                _requestedVersion++;
                if (requestId > 0 && _refreshSynchronizations.TryGetValue(requestId, out var refresh))
                    refresh.Version = _requestedVersion;
                if (_workerRunning)
                    return;
                _workerRunning = true;
                startWorker = true;
            }

            if (startWorker)
                _ = SynchronizationLoopAsync();
        }

        private async Task SynchronizationLoopAsync()
        {
            while (true)
            {
                int observedVersion;
                lock (_syncGate)
                    observedVersion = _requestedVersion;

                try
                {
                    await SynchronizeOnceAsync(notify: true, _lifetime.Token);
                    CompleteRefreshSynchronizations(observedVersion, null);
                }
                catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
                {
                    CompleteRefreshSynchronizations(
                        observedVersion,
                        new InvalidOperationException("The record subscription is no longer active."));
                    StopWorker();
                    return;
                }
                catch (PlayServRecordNotFoundException)
                {
                    var failure = new TargetNotFoundException($"Record '{_record.Id}' no longer exists.");
                    await TerminateAsync(
                        failure,
                        markDeleted: true,
                        releaseLease: true);
                    CompleteRefreshSynchronizations(observedVersion, failure);
                    StopWorker();
                    return;
                }
                catch (PlayServDataException exception)
                {
                    CompleteRefreshSynchronizations(observedVersion, exception);
                    Dispatch(() =>
                    {
                        SynchronizationError?.Invoke(exception);
                        Failure?.Invoke(exception.UnifiedError);
                    });
                }
                catch (Exception exception)
                {
                    var failure = new PlayServDataException(
                        "The realtime record snapshot could not be synchronized.",
                        backendCode: "record_realtime_sync_failed",
                        innerException: exception);
                    CompleteRefreshSynchronizations(observedVersion, failure);
                    var error = new PlayServError(
                        PlayServErrorCode.Unknown,
                        "record_realtime_sync_failed",
                        "The realtime record snapshot could not be synchronized.",
                        rawDetails: exception.Message);
                    SafeLogError("[TypedRecord] Realtime synchronization failed.");
                    Dispatch(() => Failure?.Invoke(error));
                }

                lock (_syncGate)
                {
                    if (observedVersion == _requestedVersion ||
                        Volatile.Read(ref _disposed) != 0 ||
                        Volatile.Read(ref _terminalSignaled) != 0)
                    {
                        _workerRunning = false;
                        return;
                    }
                }
            }
        }

        private async Task SynchronizeOnceAsync(bool notify, CancellationToken ct)
        {
            var applied = await _record.SynchronizeRealtimeAsync(ct);
            if (!notify || !applied.Applied || Volatile.Read(ref _disposed) != 0)
                return;

            Dispatch(() =>
            {
                if (Volatile.Read(ref _disposed) != 0 || Volatile.Read(ref _terminalSignaled) != 0)
                    return;
                if (applied.HasConflict)
                {
                    Conflict?.Invoke(new PlayServRecordRealtimeConflict<T>(
                        _record,
                        applied.LocalValue,
                        applied.RemoteValue,
                        applied.LocalChangedFields,
                        applied.RemoteChangedFields));
                }
                Changed?.Invoke(new PlayServRecordChange<T>(
                    _record,
                    applied.RemoteChangedFields,
                    applied.HasConflict));
            });
        }

        public async Task RefreshAsync(CancellationToken ct = default)
        {
            EnsureRefreshable(ct);
            RefreshSynchronization refresh = null;
            try
            {
                await _adapter.RequestTransportFullStateAsync(
                    _transportLease,
                    ct,
                    requestId => refresh = RegisterRefreshSynchronization(requestId));

                if (refresh == null || refresh.Version <= 0)
                {
                    throw new DataSubscriptionException(
                        40001,
                        "The record refresh response was not delivered to the active subscription.",
                        sourceCode: "subscription_refresh_not_applied");
                }

                await AwaitWithCancellationAsync(refresh.Completion.Task, ct);
            }
            finally
            {
                if (refresh != null)
                    RemoveRefreshSynchronization(refresh.RequestId, refresh);
            }
        }

        private void OnLeaseFailure(DataSubscriptionException exception)
        {
            if (Volatile.Read(ref _disposed) == 0)
                Dispatch(() => RaiseDataflowFailure(exception));
        }

        private void OnLeaseTerminated(DataSubscriptionException exception)
        {
            if (Volatile.Read(ref _disposed) == 0)
                _ = TerminateAsync(exception, IsDeletion(exception), releaseLease: false);
        }

        private async Task TerminateAsync(
            DataSubscriptionException exception,
            bool markDeleted,
            bool releaseLease)
        {
            if (Interlocked.Exchange(ref _terminalSignaled, 1) != 0)
                return;

            _localState = PlayServSubscriptionState.Terminated;
            _localTerminalError = exception?.UnifiedError;
            _lifetime.Cancel();
            FailRefreshSynchronizations(exception ??
                new SubscriptionTerminatedException(_transportLease.SubscriptionId));
            DetachLeaseHandlers();
            if (markDeleted)
            {
                try
                {
                    await _record.MarkDeletedFromRealtimeAsync(CancellationToken.None);
                }
                catch (Exception)
                {
                    SafeLogError("[TypedRecord] Failed to mark deleted record.");
                }
            }
            if (releaseLease)
                _transportLease.Dispose();

            Dispatch(() =>
            {
                RaiseDataflowFailure(exception);
                Terminated?.Invoke();
            });
        }

        public async Task<PlayServSubscriptionCloseResult> CloseAsync(CancellationToken ct = default)
        {
            if (Volatile.Read(ref _disposed) != 0)
                return PlayServSubscriptionCloseResult.Success(true);
            ct.ThrowIfCancellationRequested();
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return PlayServSubscriptionCloseResult.Success(true);

            _lifetime.Cancel();
            FailRefreshSynchronizations(
                new InvalidOperationException("The record subscription has been closed."));
            DetachLeaseHandlers();
            if (_localState != PlayServSubscriptionState.Terminated)
                _localState = PlayServSubscriptionState.Closed;
            return await _transportLease.CloseAsync(ct);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            _lifetime.Cancel();
            FailRefreshSynchronizations(
                new InvalidOperationException("The record subscription has been closed."));
            DetachLeaseHandlers();
            if (_localState != PlayServSubscriptionState.Terminated)
                _localState = PlayServSubscriptionState.Closed;
            _transportLease.Dispose();
        }

        private void RaiseDataflowFailure(DataSubscriptionException exception)
        {
            if (exception == null)
                return;
            Error?.Invoke(exception);
            Failure?.Invoke(exception.UnifiedError);
        }

        private void StopWorker()
        {
            lock (_syncGate)
                _workerRunning = false;
        }

        private RefreshSynchronization RegisterRefreshSynchronization(long requestId)
        {
            var refresh = new RefreshSynchronization(requestId);
            lock (_syncGate)
            {
                if (Volatile.Read(ref _disposed) != 0 || Volatile.Read(ref _terminalSignaled) != 0)
                    throw new InvalidOperationException("The record subscription is no longer active.");
                _refreshSynchronizations.Add(requestId, refresh);
            }
            return refresh;
        }

        private void RemoveRefreshSynchronization(long requestId, RefreshSynchronization refresh)
        {
            lock (_syncGate)
            {
                if (_refreshSynchronizations.TryGetValue(requestId, out var current) &&
                    ReferenceEquals(current, refresh))
                {
                    _refreshSynchronizations.Remove(requestId);
                }
            }
        }

        private void CompleteRefreshSynchronizations(int throughVersion, Exception failure)
        {
            RefreshSynchronization[] ready;
            lock (_syncGate)
            {
                ready = _refreshSynchronizations.Values
                    .Where(value => value.Version > 0 && value.Version <= throughVersion)
                    .ToArray();
            }

            foreach (var refresh in ready)
            {
                if (failure == null)
                    refresh.Completion.TrySetResult(true);
                else
                    refresh.Completion.TrySetException(failure);
            }
        }

        private void FailRefreshSynchronizations(Exception failure)
        {
            RefreshSynchronization[] pending;
            lock (_syncGate)
                pending = _refreshSynchronizations.Values.ToArray();
            foreach (var refresh in pending)
                refresh.Completion.TrySetException(failure);
        }

        private void EnsureRefreshable(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (Volatile.Read(ref _disposed) != 0 || State == PlayServSubscriptionState.Closed)
                throw new InvalidOperationException("The record subscription has been closed.");
            if (Volatile.Read(ref _terminalSignaled) != 0 || State == PlayServSubscriptionState.Terminated)
                throw new InvalidOperationException("The record subscription has been terminated.");
            if (State != PlayServSubscriptionState.Active || !_adapter.CanSendCommands)
                throw _adapter.CreateConnectionUnavailableException("Data subscription refresh");
        }

        private static async Task AwaitWithCancellationAsync(Task task, CancellationToken ct)
        {
            if (!ct.CanBeCanceled || task.IsCompleted)
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

        private void DetachLeaseHandlers()
        {
            _transportLease.Updated -= OnServerUpdate;
            _transportLease.Failed -= OnLeaseFailure;
            _transportLease.Terminated -= OnLeaseTerminated;
        }

        private void Dispatch(Action action)
        {
            if (action == null)
                return;
            if (_synchronizationContext != null &&
                !ReferenceEquals(SynchronizationContext.Current, _synchronizationContext))
            {
                _synchronizationContext.Post(_ => action(), null);
                return;
            }
            action();
        }

        private static bool IsDeletion(DataSubscriptionException exception) =>
            exception is TargetNotFoundException || exception?.ErrorCode == 49001;

        private void SafeLogError(string message)
        {
            try { _logger.LogError(message); } catch { }
        }

        private sealed class RefreshSynchronization
        {
            private int _version;

            internal RefreshSynchronization(long requestId)
            {
                RequestId = requestId;
            }

            internal long RequestId { get; }
            internal int Version
            {
                get => Volatile.Read(ref _version);
                set => Volatile.Write(ref _version, value);
            }
            internal TaskCompletionSource<bool> Completion { get; } = new TaskCompletionSource<bool>();
        }
    }
}
