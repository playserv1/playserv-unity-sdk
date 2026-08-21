using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Playserv.DataSubscription.Exceptions;
using Playserv.Proxy.Logging;
using Playserv.Serialization;
using Playserv.Wrapper;

namespace Playserv.DataSubscription
{
    /// <summary>
    /// Transport-backed collection subscription (conventions §21.2). The server sends the whole
    /// (filtered) table as an <c>Overwrite</c> frame — an array carried under the entity root field
    /// (<c>Data = { "&lt;Entity&gt;": [ …rows… ] }</c>, so the frame stays a JSON object) with
    /// <c>IsCollection = true</c> — on subscribe and again on every row change. Each frame REPLACES
    /// <see cref="Items"/> wholesale (no patching; the server re-sends the full collection).
    /// </summary>
    internal sealed class SharedCollection<TItem> : ISharedCollection<TItem>, IDisposable
    {
        private readonly long _subscriptionId;
        private readonly PlayServDataSubscriptionAdapter _adapter;
        private readonly TransportSubscriptionLease _transportLease;
        private readonly string _rootFieldName;
        private readonly IJsonCodec _jsonCodec;
        private readonly ILogger _logger = PlayServLog.ForCategory(PlayServLogCategory.Data);
        private bool _isDisposed;

        public IReadOnlyList<TItem> Items { get; private set; } = new List<TItem>();

        public event Action<IReadOnlyList<TItem>> Changed;
        public event Action<DataSubscriptionException> Error;
        public event Action<PlayServError> Failure;
        public event Action Terminated;

        public PlayServSubscriptionState State => _transportLease.State;

        public PlayServError TerminalError => _transportLease.TerminalError;

        public SharedCollection(
            PlayServDataSubscriptionAdapter adapter,
            TransportSubscriptionLease transportLease,
            string rootFieldName)
        {
            if (adapter == null) throw new ArgumentNullException(nameof(adapter));
            _adapter = adapter;
            _transportLease = transportLease ?? throw new ArgumentNullException(nameof(transportLease));
            _subscriptionId = transportLease.SubscriptionId;
            _rootFieldName = rootFieldName ?? throw new ArgumentNullException(nameof(rootFieldName));
            _jsonCodec = adapter.GetJsonCodec();
            _transportLease.Updated += OnServerUpdate;
            _transportLease.Failed += OnLeaseFailure;
            _transportLease.Terminated += OnLeaseTerminated;
            if (_transportLease.InitialUpdate != null && !_transportLease.InitialUpdate.HasError)
                ApplySnapshot(_transportLease.InitialUpdate, notify: false);
        }

        private void OnServerUpdate(DataSubscriptionUpdate update)
        {
            if (_isDisposed || update == null)
                return;

            if (update.HasError)
            {
                HandleError(update);
                return;
            }

            try
            {
                // Collection frames are always full Overwrites (the server re-sends the whole
                // filtered table on any change). Unwrap the array from under the entity root field,
                // then deserialize to the row model.
                ApplySnapshot(update, notify: true);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[SharedCollection] Failed to map collection payload: {ex.Message}");
            }
        }

        private void HandleError(DataSubscriptionUpdate update)
        {
            var code = update.ErrorCode ?? 0;
            var message = string.IsNullOrWhiteSpace(update.EffectiveErrorMessage)
                ? "Unknown subscription update error."
                : update.EffectiveErrorMessage;

            if (code == SubscriptionTerminatedException.Code)
            {
                RaiseFailure(new SubscriptionTerminatedException(_subscriptionId, message));
                Terminated?.Invoke();
            }
            else
            {
                RaiseFailure(new DataSubscriptionException(code, message));
            }
        }

        private void ApplySnapshot(DataSubscriptionUpdate update, bool notify)
        {
            var payload = DataSubscriptionErrorMapper.ExtractSubscriptionPayload(
                update.Data,
                _rootFieldName,
                _jsonCodec);
            Items = payload == null
                ? new List<TItem>()
                : _jsonCodec.Convert<List<TItem>>(payload) ?? new List<TItem>();
            if (notify)
                Changed?.Invoke(Items);
        }

        private void OnLeaseFailure(DataSubscriptionException exception)
        {
            if (!_isDisposed)
                RaiseFailure(exception);
        }

        private void OnLeaseTerminated(DataSubscriptionException exception)
        {
            if (_isDisposed)
                return;
            RaiseFailure(exception);
            Terminated?.Invoke();
        }

        public Task RefreshAsync(CancellationToken ct = default)
        {
            EnsureRefreshable(ct);
            return _adapter.RequestFullStateAsync(_transportLease, ct);
        }

        public async Task<PlayServSubscriptionCloseResult> CloseAsync(CancellationToken ct = default)
        {
            if (_isDisposed)
                return PlayServSubscriptionCloseResult.Success(true);
            ct.ThrowIfCancellationRequested();
            _isDisposed = true;
            DetachLeaseHandlers();
            return await _transportLease.CloseAsync(ct);
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            DetachLeaseHandlers();
            _transportLease.Dispose();
        }

        private void EnsureRefreshable(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (_isDisposed || State == PlayServSubscriptionState.Closed)
                throw new InvalidOperationException("The collection subscription has been closed.");
            if (State == PlayServSubscriptionState.Terminated)
                throw new InvalidOperationException("The collection subscription has been terminated.");
            if (State != PlayServSubscriptionState.Active || !_adapter.CanSendCommands)
                throw _adapter.CreateConnectionUnavailableException("Data subscription refresh");
        }

        private void RaiseFailure(DataSubscriptionException exception)
        {
            Error?.Invoke(exception);
            if (exception?.UnifiedError != null)
                Failure?.Invoke(exception.UnifiedError);
        }

        private void DetachLeaseHandlers()
        {
            _transportLease.Updated -= OnServerUpdate;
            _transportLease.Failed -= OnLeaseFailure;
            _transportLease.Terminated -= OnLeaseTerminated;
        }
    }
}
