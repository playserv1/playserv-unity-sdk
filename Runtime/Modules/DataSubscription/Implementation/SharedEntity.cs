using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Playserv.DataSubscription.Exceptions;
using Playserv.DataSubscription.JsonPatch;
using Playserv.Proxy.Logging;
using Playserv.Serialization;
using Playserv.Wrapper;

namespace Playserv.DataSubscription
{
    internal sealed class SharedEntity<T> : ISharedEntity<T>, IDisposable where T : class, new()
    {
        private readonly PlayServDataSubscriptionAdapter _adapter;
        private readonly long _subscriptionId;
        private readonly TransportSubscriptionLease _transportLease;
        private readonly LambdaExpression _selector;
        private readonly Func<object, T> _compiledSelectorMapper;
        private readonly Func<object, T> _mapFunc;
        private readonly IDisposable _subscription;
        private readonly string _query;
        private readonly Dictionary<string, object> _variables;
        private readonly IJsonCodec _jsonCodec;
        private readonly ILogger _logger = PlayServLog.ForCategory(PlayServLogCategory.Data);
        private bool _isDisposed;
        private PlayServSubscriptionState _localState = PlayServSubscriptionState.Active;
        private PlayServError _localTerminalError;

        public T Value { get; private set; } = new();

        public event Action<T> Changed;
        public event Action<DataSubscriptionException> Error;
        public event Action<PlayServError> Failure;
        public event Action Terminated;

        public PlayServSubscriptionState State => _transportLease?.State ?? _localState;

        public PlayServError TerminalError => _transportLease?.TerminalError ?? _localTerminalError;

        /// <summary>
        /// Creates shared entity backed by transport DataSubscriptionRequest/DataSubscriptionUpdate flow.
        /// </summary>
        public SharedEntity(
            PlayServDataSubscriptionAdapter adapter,
            TransportSubscriptionLease transportLease,
            LambdaExpression selector,
            string query,
            Dictionary<string, object> variables,
            Func<object, T> mapFunc = null)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _transportLease = transportLease ?? throw new ArgumentNullException(nameof(transportLease));
            _subscriptionId = transportLease.SubscriptionId;
            _selector = selector;
            _query = query ?? throw new ArgumentNullException(nameof(query));
            _variables = variables ?? throw new ArgumentNullException(nameof(variables));
            _mapFunc = mapFunc;
            _jsonCodec = _adapter.GetJsonCodec();
            _compiledSelectorMapper = _selector == null
                ? null
                : SharedDataMapper.CreateCompiledMapper<T>(_selector, _jsonCodec);

            _transportLease.Updated += OnServerUpdate;
            _transportLease.Failed += OnLeaseFailure;
            _transportLease.Terminated += OnLeaseTerminated;
            if (_transportLease.InitialUpdate != null && !_transportLease.InitialUpdate.HasError)
                ApplyInitialUpdate(_transportLease.InitialUpdate);
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
                RaiseFailure(new DataSubscriptionException(0, "Subscription update is null."));
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
                RaiseFailure(ex);
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

            RaiseFailure(ex);

            if (ex is SubscriptionTerminatedException || ex is TargetNotFoundException)
            {
                _localState = PlayServSubscriptionState.Terminated;
                _localTerminalError = ex.UnifiedError;
                Terminated?.Invoke();
            }
        }

        private void ApplyOverwrite(object rawData)
        {
            Value = MapData(rawData);
            Changed?.Invoke(Value);
        }

        private void ApplyInitialUpdate(DataSubscriptionUpdate update)
        {
            try
            {
                Value = MapData(update.Data);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[SharedEntity] Failed to map initial subscription snapshot: {ex.Message}");
            }
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
            var errorMessage = string.IsNullOrWhiteSpace(update.EffectiveErrorMessage)
                ? "Unknown subscription update error."
                : update.EffectiveErrorMessage;

            switch (errorCode)
            {
                case UpdateDataCorruptionException.Code:
                    var corruptionEx = new UpdateDataCorruptionException(errorMessage);
                    RaiseFailure(corruptionEx);
                    RequestRefreshOnPatchFailure();
                    break;

                case SubscriptionTerminatedException.Code:
                    var terminatedEx = new SubscriptionTerminatedException(_subscriptionId, errorMessage);
                    RaiseFailure(terminatedEx);
                    Terminated?.Invoke();
                    break;

                case SubscriptionNotFoundException.Code:
                    var notFound = new SubscriptionNotFoundException(_subscriptionId, errorMessage);
                    RaiseFailure(notFound);
                    break;

                default:
                    RaiseFailure(new DataSubscriptionException(errorCode, errorMessage));
                    break;
            }
        }

        private void RequestRefreshOnPatchFailure()
        {
            if (_isDisposed)
                return;

            try
            {
                if (_transportLease != null)
                    _adapter.RequestFullState(_transportLease);
                else
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

            if (!_adapter.CanSendCommands)
            {
                RaiseFailure(_adapter.CreateConnectionUnavailableException("Data mutation"));
                return;
            }

            mutator(Value);
            var patch = SharedDiffBuilder.BuildPatch(Value);
            if (_transportLease != null)
                _adapter.SendMutation(_transportLease, _query, _variables, patch);
            else
                _adapter.SendMutation(_subscriptionId, _query, _variables, patch);
        }

        public async Task UpdateAsync(Action<T> mutator)
        {
            if (mutator == null)
                throw new ArgumentNullException(nameof(mutator));

            if (!_adapter.CanSendCommands)
                throw _adapter.CreateConnectionUnavailableException("Data mutation");

            mutator(Value);
            var patch = SharedDiffBuilder.BuildPatch(Value);
            if (_transportLease != null)
                await _adapter.SendMutationAsync(_transportLease, _query, _variables, patch);
            else
                await _adapter.SendMutationAsync(_subscriptionId, _query, _variables, patch);
        }

        public void Refresh()
        {
            if (_transportLease != null)
                _adapter.RequestFullState(_transportLease);
            else
                _adapter.RequestFullState(_subscriptionId);
        }

        public Task RefreshAsync()
        {
            return RefreshAsync(CancellationToken.None);
        }

        public Task RefreshAsync(CancellationToken ct)
        {
            EnsureRefreshable(ct);
            return _transportLease != null
                ? _adapter.RequestFullStateAsync(_transportLease, ct)
                : _adapter.RequestFullStateAsync(_subscriptionId, ct);
        }

        public async Task<PlayServSubscriptionCloseResult> CloseAsync(CancellationToken ct = default)
        {
            if (_isDisposed)
                return PlayServSubscriptionCloseResult.Success(true);
            ct.ThrowIfCancellationRequested();

            _isDisposed = true;
            if (_transportLease == null && _localState != PlayServSubscriptionState.Terminated)
                _localState = PlayServSubscriptionState.Closed;
            DetachTransportLeaseHandlers();
            if (_transportLease != null)
                return await _transportLease.CloseAsync(ct);
            _subscription?.Dispose();
            return PlayServSubscriptionCloseResult.Success();
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            if (_transportLease == null && _localState != PlayServSubscriptionState.Terminated)
                _localState = PlayServSubscriptionState.Closed;
            DetachTransportLeaseHandlers();
            if (_transportLease != null)
                _transportLease.Dispose();
            else
                _subscription?.Dispose();
        }

        private void EnsureRefreshable(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (_isDisposed || State == PlayServSubscriptionState.Closed)
                throw new InvalidOperationException("The shared entity subscription has been closed.");
            if (State == PlayServSubscriptionState.Terminated)
                throw new InvalidOperationException("The shared entity subscription has been terminated.");
            if (State != PlayServSubscriptionState.Active || !_adapter.CanSendCommands)
                throw _adapter.CreateConnectionUnavailableException("Data subscription refresh");
        }

        private void RaiseFailure(DataSubscriptionException exception)
        {
            Error?.Invoke(exception);
            if (exception?.UnifiedError != null)
                Failure?.Invoke(exception.UnifiedError);
        }

        private void DetachTransportLeaseHandlers()
        {
            if (_transportLease == null)
                return;
            _transportLease.Updated -= OnServerUpdate;
            _transportLease.Failed -= OnLeaseFailure;
            _transportLease.Terminated -= OnLeaseTerminated;
        }
    }
}
