using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Playserv.DataSubscription.Exceptions;
using Playserv.DataSubscription.JsonPatch;
using Playserv.Proxy.Logging;

namespace Playserv.DataSubscription
{
    internal sealed class SharedEntity<T> : ISharedEntity<T>, IDisposable where T : class, new()
    {
        private readonly PlayServDataSubscriptionAdapter _adapter;
        private readonly long _subscriptionId;
        private readonly LambdaExpression _selector;
        private readonly Func<object, T> _mapFunc;
        private readonly IDisposable _subscription;
        private readonly string _query;
        private readonly Dictionary<string, object> _variables;
        private bool _isDisposed;
        private readonly ILogger _logger = new ConsoleLogger();

        public T Value { get; private set; } = new();

        public event Action<T> Changed;
        public event Action<DataSubscriptionException> Error;
        public event Action Terminated;

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
            _subscription = _adapter.OnSubscriptionUpdate(_subscriptionId, OnServerUpdate);
        }

        private void OnServerUpdate(DataSubscriptionUpdate update)
        {
            if (_isDisposed)
                return;

            if (update.HasError)
            {
                HandleError(update);
                return;
            }

            try
            {
                if (update.IsOverwrite)
                {
                    ApplyOverwrite(update.Data);
                }
                else if (update.IsPatch)
                {
                    ApplyPatch(update.Data, update.IsCollection);
                }
            }
            catch (UpdateDataCorruptionException ex)
            {
                _logger.LogWarning($"[SharedEntity] Patch failed, requesting full refresh: {ex.Message}");
                Error?.Invoke(ex);
                RequestRefreshOnPatchFailure();
            }
            catch (Exception ex)
            {
                _logger.LogError($"[SharedEntity] Unexpected error processing update: {ex.Message}");
            }
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
                _logger.LogWarning("[SharedEntity] Cannot apply patch to null value, requesting full refresh");
                RequestRefreshOnPatchFailure();
                return;
            }

            Value = JsonPatchApplier.ApplyPatch(Value, patchData);
            Changed?.Invoke(Value);
        }

        private T MapData(object raw)
        {
            if (_mapFunc != null)
                return _mapFunc(raw);

            if (_selector != null)
                return SharedDataMapper.Map<T>(_selector, raw);

            var json = raw is string str ? str : JsonConvert.SerializeObject(raw);
            return JsonConvert.DeserializeObject<T>(json);
        }

        private void HandleError(DataSubscriptionUpdate update)
        {
            var errorCode = update.ErrorCode ?? 0;
            var errorMessage = update.ErrorMessage ?? "Unknown error";

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
                    _logger.LogWarning($"[SharedEntity] Subscription not found (race condition): {errorMessage}");
                    break;

                default:
                    _logger.LogWarning($"[SharedEntity] Unknown error code {errorCode}: {errorMessage}");
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
