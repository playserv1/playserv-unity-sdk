using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Playserv.DataSubscription.Exceptions;
using Playserv.DataSubscription.Requests;
using Playserv.DataSubscription.Responses;

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
        private long _requestIdCounter;

        public SharedEntityBuilder(PlayServDataSubscriptionAdapter adapter, string entityType)
        {
            _adapter = adapter;
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

        public async Task<ISharedEntity<T>> BindAsync()
        {
            return await BindAsync<T>();
        }

        public async Task<ISharedEntity<TResult>> BindAsync<TResult>() where TResult : class, new()
        {
            if (_key == null)
                throw new InvalidOperationException("Key must be specified using Key() method");

            var requestId = Interlocked.Increment(ref _requestIdCounter);
            Expression<Func<T, object>> querySelector = null;

            if (_selector != null)
            {
                if (_selector is Expression<Func<T, TResult>> typedSelector)
                {
                    querySelector = x => typedSelector.Compile()(x);
                }
                else
                {
                    querySelector = x => _selector.Compile().DynamicInvoke(x);
                }
            }

            var query = QueryBuilder.BuildQuery<T>(_entityType, _key, querySelector);
            var variables = QueryBuilder.BuildVariables(_key);

            var request = new DataSubscriptionRequest(requestId, query, variables);
            var response = await _adapter.SendSubscriptionRequestAsync(request);

            if (response.HasError)
            {
                ThrowExceptionForError(response.Error);
            }

            if (response.Result == null)
                throw new InvalidOperationException("Invalid DataSubscriptionResponse: Result is null");

            var subscriptionId = response.Result.SubscriptionId;

            var entity = new SharedEntity<TResult>(
                _adapter,
                subscriptionId,
                _selector,
                query,
                variables);

            await _adapter.RequestFullStateAsync(subscriptionId);

            return entity;
        }

        private static void ThrowExceptionForError(DataSubscriptionError error)
        {
            if (error == null)
                return;

            switch (error.ErrorCode)
            {
                case 30001:
                    throw new InvalidQuerySyntaxException(error.Message);
                case 31001:
                    throw new AccessDeniedException(error.Message);
                case 31002:
                    throw new TargetNotFoundException(error.Message);
                case 39001:
                    throw new MaxSubscriptionsReachedException(error.Message);
                default:
                    throw new DataSubscriptionException(error.ErrorCode, error.Message);
            }
        }
    }
}
