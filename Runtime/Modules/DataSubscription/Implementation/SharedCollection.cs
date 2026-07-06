using System;
using System.Collections.Generic;
using Playserv.DataSubscription.Exceptions;
using Playserv.Proxy.Logging;
using Playserv.Serialization;

namespace Playserv.DataSubscription
{
    /// <summary>
    /// Transport-backed collection subscription (conventions §21.2). The server sends the whole
    /// (filtered) table as an <c>Overwrite</c> frame — an array carried under the entity root field
    /// (<c>Data = { "&lt;Entity&gt;": [ …rows… ] }</c>, so the frame stays a JSON object) with
    /// <c>IsCollection = true</c> — on subscribe and again on every row change. Each frame REPLACES
    /// <see cref="Items"/> wholesale (no patching; the server re-sends the full collection), so there
    /// is nothing to reconcile and no refresh path.
    /// </summary>
    internal sealed class SharedCollection<TItem> : ISharedCollection<TItem>, IDisposable
        where TItem : class, new()
    {
        private readonly long _subscriptionId;
        private readonly string _rootFieldName;
        private readonly IJsonCodec _jsonCodec;
        private readonly IDisposable _subscription;
        private readonly ILogger _logger = PlayServLog.ForCategory(PlayServLogCategory.Data);
        private bool _isDisposed;

        public IReadOnlyList<TItem> Items { get; private set; } = new List<TItem>();

        public event Action<IReadOnlyList<TItem>> Changed;
        public event Action<DataSubscriptionException> Error;
        public event Action Terminated;

        public SharedCollection(PlayServDataSubscriptionAdapter adapter, long subscriptionId, string rootFieldName)
        {
            if (adapter == null) throw new ArgumentNullException(nameof(adapter));
            _subscriptionId = subscriptionId;
            _rootFieldName = rootFieldName ?? throw new ArgumentNullException(nameof(rootFieldName));
            _jsonCodec = adapter.GetJsonCodec();
            _subscription = adapter.OnSubscriptionUpdate(_subscriptionId, OnServerUpdate);
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
                var payload = DataSubscriptionErrorMapper.ExtractSubscriptionPayload(update.Data, _rootFieldName, _jsonCodec);
                Items = payload == null
                    ? new List<TItem>()
                    : _jsonCodec.Convert<List<TItem>>(payload) ?? new List<TItem>();
                Changed?.Invoke(Items);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[SharedCollection] Failed to map collection payload: {ex.Message}");
            }
        }

        private void HandleError(DataSubscriptionUpdate update)
        {
            var code = update.ErrorCode ?? 0;
            var message = string.IsNullOrWhiteSpace(update.ErrorMessage)
                ? "Unknown subscription update error."
                : update.ErrorMessage;

            if (code == SubscriptionTerminatedException.Code)
            {
                Error?.Invoke(new SubscriptionTerminatedException(_subscriptionId, message));
                Terminated?.Invoke();
            }
            else
            {
                Error?.Invoke(new DataSubscriptionException(code, message));
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            _subscription?.Dispose();
        }
    }
}
