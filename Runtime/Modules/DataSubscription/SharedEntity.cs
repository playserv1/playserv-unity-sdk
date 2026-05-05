#if !PLAYSERV_DISABLE_DATA && !PLAYSERV_DISABLE_EVENTS
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Playserv.DataSubscription.Exceptions;
using Playserv.DataSubscription.JsonPatch;
using Playserv.Proxy.Logging;
using Playserv.Serialization;

namespace Playserv.DataSubscription
{
    internal sealed class SharedEntity<T> : ISharedEntity<T>, IDisposable where T : class, new()
    {
        private readonly PlayServDataSubscriptionAdapter _adapter;
        private readonly long _subscriptionId;
        private readonly LambdaExpression _selector;
        private readonly Func<object, T> _compiledSelectorMapper;
        private readonly Func<object, T> _mapFunc;
        private readonly IDisposable _subscription;
        private readonly string _query;
        private readonly Dictionary<string, object> _variables;
        private readonly IJsonCodec _jsonCodec;
        private readonly ILogger _logger = PlayServLog.ForCategory(PlayServLogCategory.Data);
        private bool _isDisposed;

        public T Value { get; private set; } = new();

        public event Action<T> Changed;
        public event Action<DataSubscriptionException> Error;
        public event Action Terminated;

        /// <summary>
        /// Creates shared entity backed by transport DataSubscriptionRequest/DataSubscriptionUpdate flow.
        /// </summary>
        public SharedEntity(
            PlayServDataSubscriptionAdapter adapter,
            long subscriptionId,
            LambdaExpression selector,
            string query,
            Dictionary<string, object> variables,
            Func<object, T> mapFunc = null)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _subscriptionId = subscriptionId;
            _selector = selector;
            _query = query ?? throw new ArgumentNullException(nameof(query));
            _variables = variables ?? throw new ArgumentNullException(nameof(variables));
            _mapFunc = mapFunc;
            _jsonCodec = _adapter.GetJsonCodec();
            _compiledSelectorMapper = _selector == null
                ? null
                : SharedDataMapper.CreateCompiledMapper<T>(_selector, _jsonCodec);

            _subscription = _adapter.OnSubscriptionUpdate(_subscriptionId, OnServerUpdate);
        }

        /// <summary>
        /// Creates shared entity backed by polling DataGet requests.
        /// </summary>
        public SharedEntity(
            PlayServDataSubscriptionAdapter adapter,
            long subscriptionId,
            string key,
            string rootFieldName,
            LambdaExpression selector,
            string query,
            Dictionary<string, object> variables,
            Func<object, T> mapFunc = null)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Subscription key is required.", nameof(key));

            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _subscriptionId = subscriptionId;
            _selector = selector;
            _query = query ?? throw new ArgumentNullException(nameof(query));
            _variables = variables ?? throw new ArgumentNullException(nameof(variables));
            _mapFunc = mapFunc;
            _jsonCodec = _adapter.GetJsonCodec();
            _compiledSelectorMapper = _selector == null
                ? null
                : SharedDataMapper.CreateCompiledMapper<T>(_selector, _jsonCodec);

            _subscription = _adapter.RegisterPollingSubscription(
                _subscriptionId,
                key,
                _query,
                _variables,
                rootFieldName,
                OnPollingDataReceived,
                OnPollingErrorReceived);
        }

        private void OnServerUpdate(DataSubscriptionUpdate update)
        {
            if (_isDisposed)
                return;

            if (update == null)
            {
                Error?.Invoke(new DataSubscriptionException(0, "Subscription update is null."));
                return;
            }

            if (update.HasError)
            {
                HandleTransportUpdateError(update);
                return;
            }

            try
            {
                if (update.IsOverwrite)
                {
                    ApplyOverwrite(update.Data);
                    return;
                }

                if (update.IsPatch)
                {
                    ApplyPatch(update.Data, update.IsCollection);
                    return;
                }

                ApplyOverwrite(update.Data);
            }
            catch (UpdateDataCorruptionException ex)
            {
                _logger.LogWarning($"[SharedEntity] Patch failed, requesting full refresh: {ex.Message}");
                Error?.Invoke(ex);
                RequestRefreshOnPatchFailure();
            }
            catch (Exception ex)
            {
                _logger.LogError($"[SharedEntity] Unexpected error processing transport update: {ex.Message}");
            }
        }

        private void OnPollingDataReceived(object rawData)
        {
            if (_isDisposed)
                return;

            try
            {
                Value = MapData(rawData);
                Changed?.Invoke(Value);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[SharedEntity] Failed to map polling payload: {ex.Message}");
            }
        }

        private void OnPollingErrorReceived(DataSubscriptionException ex)
        {
            if (_isDisposed)
                return;

            Error?.Invoke(ex);

            if (ex is SubscriptionTerminatedException || ex is TargetNotFoundException)
                Terminated?.Invoke();
        }

        private void ApplyOverwrite(object rawData)
        {
            Value = MapData(rawData);
            Changed?.Invoke(Value);
        }

        private void ApplyPatch(object patchData, bool isCollection)
        {
            if (Value == null)
            {
                _logger.LogWarning("[SharedEntity] Cannot apply patch to null value, requesting full refresh.");
                RequestRefreshOnPatchFailure();
                return;
            }

            Value = JsonPatchApplier.ApplyPatch(Value, patchData, _jsonCodec);
            Changed?.Invoke(Value);
        }

        private T MapData(object raw)
        {
            if (raw == null)
                return new T();

            if (_mapFunc != null)
                return _mapFunc(raw);

            if (_compiledSelectorMapper != null)
                return _compiledSelectorMapper(raw);

            var mapped = _jsonCodec.Convert<T>(raw);
            return mapped ?? new T();
        }

        private void HandleTransportUpdateError(DataSubscriptionUpdate update)
        {
            var errorCode = update.ErrorCode ?? 0;
            var errorMessage = string.IsNullOrWhiteSpace(update.ErrorMessage)
                ? "Unknown subscription update error."
                : update.ErrorMessage;

            switch (errorCode)
            {
                case UpdateDataCorruptionException.Code:
                    var corruptionEx = new UpdateDataCorruptionException(errorMessage);
                    Error?.Invoke(corruptionEx);
                    RequestRefreshOnPatchFailure();
                    break;

                case SubscriptionTerminatedException.Code:
                    var terminatedEx = new SubscriptionTerminatedException(_subscriptionId, errorMessage);
                    Error?.Invoke(terminatedEx);
                    Terminated?.Invoke();
                    break;

                case SubscriptionNotFoundException.Code:
                    var notFound = new SubscriptionNotFoundException(_subscriptionId, errorMessage);
                    Error?.Invoke(notFound);
                    break;

                default:
                    Error?.Invoke(new DataSubscriptionException(errorCode, errorMessage));
                    break;
            }
        }

        private void RequestRefreshOnPatchFailure()
        {
            if (_isDisposed)
                return;

            try
            {
                _adapter.RequestFullState(_subscriptionId);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[SharedEntity] Failed to request full refresh: {ex.Message}");
            }
        }

        public void Update(Action<T> mutator)
        {
            if (mutator == null)
                throw new ArgumentNullException(nameof(mutator));

            mutator(Value);
            var patch = SharedDiffBuilder.BuildPatch(Value);
            _adapter.SendMutation(_subscriptionId, _query, _variables, patch);
        }

        public async Task UpdateAsync(Action<T> mutator)
        {
            if (mutator == null)
                throw new ArgumentNullException(nameof(mutator));

            mutator(Value);
            var patch = SharedDiffBuilder.BuildPatch(Value);
            await _adapter.SendMutationAsync(_subscriptionId, _query, _variables, patch);
        }

        public void Refresh()
        {
            _adapter.RequestFullState(_subscriptionId);
        }

        public Task RefreshAsync()
        {
            return _adapter.RequestFullStateAsync(_subscriptionId);
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

#endif
