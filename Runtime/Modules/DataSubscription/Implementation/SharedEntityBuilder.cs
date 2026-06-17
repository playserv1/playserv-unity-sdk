using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace Playserv.DataSubscription
{
    internal sealed class SharedEntityBuilder<T> : ISharedEntityBuilder<T> where T : class, new()
    {
        private readonly PlayServDataSubscriptionAdapter _adapter;
        private readonly string _entityType;
        private object _key;
        private Expression<Func<T, bool>> _wherePredicate;
        private readonly List<Expression> _includes = new List<Expression>();
        private LambdaExpression _selector;
        private DataSubscriptionMode _mode = DataSubscriptionMode.Polling;

        public SharedEntityBuilder(PlayServDataSubscriptionAdapter adapter, string entityType)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _entityType = entityType ?? typeof(T).Name;
        }

        public ISharedEntityBuilder<T> Key(object id)
        {
            _key = id;
            return this;
        }

        public ISharedEntityBuilder<T> UseTransport()
        {
            _mode = DataSubscriptionMode.Transport;
            return this;
        }

        public ISharedEntityBuilder<T> UsePolling()
        {
            _mode = DataSubscriptionMode.Polling;
            return this;
        }

        public ISharedEntityBuilder<T> Where(Expression<Func<T, bool>> predicate)
        {
            _wherePredicate = predicate;
            return this;
        }

        public ISharedEntityBuilder<T> Include<TProp>(Expression<Func<T, TProp>> nav)
        {
            _includes.Add(nav);
            return this;
        }

        public ISharedEntityBuilder<TResult> Select<TResult>(Expression<Func<T, TResult>> selector) where TResult : class, new()
        {
            var builder = new SharedEntityBuilder<TResult>(_adapter, _entityType);
            builder.SetKey(_key);
            builder.SetSelector(selector);
            builder.SetMode(_mode);
            return builder;
        }

        internal void SetKey(object key)
        {
            _key = key;
        }

        internal void SetSelector(LambdaExpression selector)
        {
            _selector = selector;
        }

        internal void SetMode(DataSubscriptionMode mode)
        {
            _mode = mode;
        }

        public Task<ISharedEntity<T>> BindAsync()
        {
            return BindAsync<T>();
        }

        public async Task<ISharedEntity<TResult>> BindAsync<TResult>() where TResult : class, new()
        {
            if (_key == null)
                throw new InvalidOperationException("Key must be specified using Key() method");

            var stringKey = ConvertKeyToString(_key);
            var query = QueryBuilder.BuildQuery<T>(_entityType, _key, jsonCodec: _adapter.GetJsonCodec());
            var variables = QueryBuilder.BuildVariables(_key);

            if (_mode == DataSubscriptionMode.Transport)
            {
                var transportSubscriptionId = await _adapter.TryOpenTransportSubscriptionAsync(
                    query,
                    variables,
                    allowFallbackToPolling: false);

                if (transportSubscriptionId.HasValue)
                {
                    var transportEntity = new SharedEntity<TResult>(
                        _adapter,
                        transportSubscriptionId.Value,
                        _selector,
                        query,
                        variables);

                    return transportEntity;
                }
            }

            var pollingSubscriptionId = _adapter.NextSubscriptionId();
            var pollingEntity = new SharedEntity<TResult>(
                _adapter,
                pollingSubscriptionId,
                stringKey,
                _entityType,
                _selector,
                query,
                variables);

            return pollingEntity;
        }

        private static string ConvertKeyToString(object key)
        {
            if (key is string text && !string.IsNullOrWhiteSpace(text))
                return text;

            var value = key?.ToString();
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException("Key must be a non-empty string value for polling requests.");

            return value;
        }
    }
}
