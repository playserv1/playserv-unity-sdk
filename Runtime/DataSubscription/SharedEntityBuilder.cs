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

        public Task<ISharedEntity<T>> BindAsync()
        {
            return BindAsync<T>();
        }

        public Task<ISharedEntity<TResult>> BindAsync<TResult>() where TResult : class, new()
        {
            if (_key == null)
                throw new InvalidOperationException("Key must be specified using Key() method");

            var stringKey = ConvertKeyToString(_key);
            var query = QueryBuilder.BuildQuery<T>(_entityType, _key);
            var variables = QueryBuilder.BuildVariables(_key);
            var subscriptionId = _adapter.NextSubscriptionId();

            var entity = new SharedEntity<TResult>(
                _adapter,
                subscriptionId,
                stringKey,
                _entityType,
                _selector,
                query,
                variables);

            return Task.FromResult<ISharedEntity<TResult>>(entity);
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
