using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Playserv.DataSubscription.Exceptions;
using Playserv.Proxy.Logging;

namespace Playserv.DataSubscription
{
    internal sealed class SharedEntity<T> : ISharedEntity<T>, IDisposable where T : class, new()
    {
        private readonly PlayServDataSubscriptionAdapter _adapter;
        private readonly long _subscriptionId;
        private readonly LambdaExpression? _selector;
        private readonly Func<object?, T>? _mapFunc;
        private readonly IDisposable _subscription;
        private readonly string _query;
        private readonly Dictionary<string, object> _variables;
        private readonly ILogger _logger = new ConsoleLogger();
        private bool _isDisposed;

        public T Value { get; private set; } = new();

        public event Action<T> Changed;
        public event Action<DataSubscriptionException> Error;
        public event Action Terminated;

        public SharedEntity(
            PlayServDataSubscriptionAdapter adapter,
            long subscriptionId,
            string key,
            string rootFieldName,
            LambdaExpression? selector,
            string query,
            Dictionary<string, object> variables,
            Func<object?, T>? mapFunc = null)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Subscription key is required.", nameof(key));

            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _subscriptionId = subscriptionId;
            _selector = selector;
            _query = query ?? throw new ArgumentNullException(nameof(query));
            _variables = variables ?? throw new ArgumentNullException(nameof(variables));
            _mapFunc = mapFunc;

            _subscription = _adapter.RegisterPollingSubscription(
                _subscriptionId,
                key,
                _query,
                _variables,
                rootFieldName,
                OnPollingDataReceived,
                OnPollingErrorReceived);
        }

        private void OnPollingDataReceived(object? rawData)
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

        private T MapData(object? raw)
        {
            if (raw == null)
                return new T();

            if (_mapFunc != null)
                return _mapFunc(raw);

            if (_selector != null)
                return SharedDataMapper.Map<T>(_selector, raw);

            var json = raw is string str ? str : JsonConvert.SerializeObject(raw);
            var mapped = JsonConvert.DeserializeObject<T>(json);
            return mapped ?? new T();
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
