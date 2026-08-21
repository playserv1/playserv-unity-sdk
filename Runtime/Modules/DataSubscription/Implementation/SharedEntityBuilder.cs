using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Playserv.Data;

namespace Playserv.DataSubscription
{
    internal sealed class SharedEntityBuilder<T> : ISharedEntityBuilder<T> where T : class, new()
    {
        private readonly PlayServDataSubscriptionAdapter _adapter;
        private readonly string _entityType;
        private Type _modelType;
        private object _key;
        private LambdaExpression _wherePredicate;
        private readonly List<LambdaExpression> _includes = new List<LambdaExpression>();
        private LambdaExpression _selector;
        private DataSubscriptionMode _mode = DataSubscriptionMode.Polling;

        public SharedEntityBuilder(PlayServDataSubscriptionAdapter adapter, string entityType)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _entityType = entityType ?? typeof(T).Name;
            _modelType = typeof(T);
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

        [Obsolete("Keyed entity subscriptions cannot apply Where filters. Use PlayServData.Records<T>().SubscribeAsync(query) for filtered collections.")]
        public ISharedEntityBuilder<T> Where(Expression<Func<T, bool>> predicate)
        {
            if (predicate == null)
                throw new ArgumentNullException(nameof(predicate));
            _wherePredicate = predicate;
            return this;
        }

        public ISharedEntityBuilder<T> Include<TProp>(Expression<Func<T, TProp>> nav)
        {
            if (nav == null)
                throw new ArgumentNullException(nameof(nav));
            _includes.Add(nav);
            return this;
        }

        public ISharedEntityBuilder<TResult> Select<TResult>(Expression<Func<T, TResult>> selector) where TResult : class, new()
        {
            if (selector == null)
                throw new ArgumentNullException(nameof(selector));

            var builder = new SharedEntityBuilder<TResult>(_adapter, _entityType);
            builder.SetKey(_key);
            builder.SetSelector(selector);
            builder.SetMode(_mode);
            builder.SetModelType(_modelType);
            builder.SetWhere(_wherePredicate);
            builder.SetIncludes(_includes);
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

        internal void SetModelType(Type modelType)
        {
            _modelType = modelType ?? throw new ArgumentNullException(nameof(modelType));
        }

        internal void SetWhere(LambdaExpression predicate)
        {
            _wherePredicate = predicate;
        }

        internal void SetIncludes(IEnumerable<LambdaExpression> includes)
        {
            _includes.Clear();
            if (includes != null)
                _includes.AddRange(includes);
        }

        public Task<ISharedEntity<T>> BindAsync()
        {
            return BindAsync<T>();
        }

        public async Task<ISharedEntity<TResult>> BindAsync<TResult>() where TResult : class, new()
        {
            if (_key == null)
                throw new InvalidOperationException("Key must be specified using Key() method");
            if (_wherePredicate != null)
            {
                throw new PlayServQueryCapabilityException(
                    PlayServQueryTarget.KeyedEntitySubscription,
                    new[] { "Where" });
            }

            var stringKey = ConvertKeyToString(_key);
            var query = QueryBuilder.BuildKeyedQuery(
                _entityType,
                _key,
                _modelType,
                _selector,
                _includes,
                _adapter.GetJsonCodec());
            var variables = QueryBuilder.BuildVariables(_key);

            if (_mode == DataSubscriptionMode.Transport)
            {
                var transportLease = await _adapter.AcquireTransportSubscriptionAsync(
                    query,
                    variables,
                    _entityType);

                return new SharedEntity<TResult>(
                    _adapter,
                    transportLease,
                    _selector,
                    query,
                    variables);
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
