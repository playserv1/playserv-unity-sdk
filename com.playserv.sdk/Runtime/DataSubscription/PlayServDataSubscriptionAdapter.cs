using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Playserv.DataSubscription.Exceptions;
using Playserv.DataSubscription.Requests;
using Playserv.DataSubscription.Responses;
using Playserv.Events.Responses;
using Playserv.Proxy.Common;
using UnityEngine;
using ILogger = Playserv.Proxy.Logging.ILogger;

namespace Playserv.DataSubscription
{
    internal sealed class PlayServDataSubscriptionAdapter : IDataSubscriptionAdapter
    {
        private readonly PlayServImplementation _transport;
        private readonly ILogger _logger;
        private long _requestIdCounter;

        public PlayServDataSubscriptionAdapter(PlayServImplementation proxy, ILogger logger)
        {
            _transport = proxy ?? throw new ArgumentNullException(nameof(proxy));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public IDisposable OnSubscriptionData(long subscriptionId, Action<object> onData)
        {
            return _transport.On<DataSubscriptionUpdate>(update =>
            {
                if (update is DataSubscriptionUpdate dataUpdate && dataUpdate.DataSubscriptionId == subscriptionId)
                {
                    onData(dataUpdate.Data);
                }
            });
        }

        public IDisposable OnSubscriptionUpdate(long subscriptionId, Action<DataSubscriptionUpdate> onUpdate)
        {
            return _transport.On<DataSubscriptionUpdate>(update =>
            {
                if (update is DataSubscriptionUpdate dataUpdate && dataUpdate.DataSubscriptionId == subscriptionId)
                {
                    onUpdate(dataUpdate);
                }
            });
        }

        public void SendMutation(long subscriptionId, string query, Dictionary<string, object> variables, object patch)
        {
            _ = SendMutationAsync(subscriptionId, query, variables, patch);
        }

        public async Task SendMutationAsync(long subscriptionId, string query, Dictionary<string, object> variables, object patch)
        {
            var requestId = Interlocked.Increment(ref _requestIdCounter);
            var mutationRequest = new DataMutationRequest
            {
                RequestId = requestId,
                Query = query,
                Variables = variables,
                UpdateType = "Overwrite",
                Data = patch
            };

            await _transport.SendAsync(mutationRequest, "module_dataflow");
        }

        public void RequestFullState(long subscriptionId)
        {
            _ = RequestFullStateAsync(subscriptionId);
        }

        public Task RequestFullStateAsync(long subscriptionId)
        {
            var requestId = Interlocked.Increment(ref _requestIdCounter);
            var refreshRequest = new DataSubscriptionRefreshRequest
            {
                RequestId = requestId,
                SubscriptionId = subscriptionId
            };

            return _transport.SendAsync(refreshRequest, "module_dataflow");
        }

        public async Task<DataSubscriptionResponse> SendSubscriptionRequestAsync(DataSubscriptionRequest request)
        {
            var tcs = new TaskCompletionSource<DataSubscriptionResponse>();
            IDisposable responseSubscription = null;
            IDisposable errorSubscription = null;

            responseSubscription = _transport.On<DataSubscriptionResponse>(response =>
            {
                if (response is DataSubscriptionResponse dataResponse && dataResponse.RequestId == request.RequestId)
                {
                    if (!tcs.Task.IsCompleted)
                    {
                        tcs.TrySetResult(dataResponse);
                    }
                    responseSubscription?.Dispose();
                    errorSubscription?.Dispose();
                }
            });

            errorSubscription = _transport.OnCommand("ErrorResponse", command =>
            {
                if (command is ErrorResponse errorResponse)
                {
                    var errorResponseWrapper = new DataSubscriptionResponse
                    {
                        RequestId = request.RequestId,
                        Error = new DataSubscriptionError
                        {
                            ErrorCode = errorResponse.ErrorCode,
                            Message = errorResponse.Message
                        }
                    };
                    if (!tcs.Task.IsCompleted)
                    {
                        tcs.TrySetResult(errorResponseWrapper);
                    }
                    responseSubscription?.Dispose();
                    errorSubscription?.Dispose();
                }
            });

            try
            {
                await _transport.SendAsync(request, "module_dataflow");
                return await tcs.Task;
            }
            finally
            {
                responseSubscription?.Dispose();
                errorSubscription?.Dispose();
            }
        }

        public async Task<ISharedEntity<TDto>> SelectEntity<TEntity, TDto>(string playerId, Func<TEntity, TDto> map)
            where TEntity : class
            where TDto : class, new()
        {
            var entityTypeName = typeof(TEntity).Name;
            var requestId = Interlocked.Increment(ref _requestIdCounter);

            var query = QueryBuilder.BuildQuery<TEntity>(entityTypeName, playerId);
            var variables = QueryBuilder.BuildVariables(playerId);

            var request = new DataSubscriptionRequest(requestId, query, variables);

            var tcs = new TaskCompletionSource<DataSubscriptionResponse>();
            IDisposable subscription = null;

            subscription = _transport.On<DataSubscriptionResponse>(response =>
            {
                if (response is DataSubscriptionResponse dataResponse && dataResponse.RequestId == requestId)
                {
                    tcs.TrySetResult(dataResponse);
                    subscription?.Dispose();
                }
            });

            try
            {
                await _transport.SendAsync(request, "module_dataflow");

                var response = await tcs.Task;

                if (response.HasError)
                {
                    throw MapErrorToException(response.Error);
                }

                if (response.Result == null)
                    throw new InvalidOperationException("Invalid DataSubscriptionResponse: Result is null");

                var subscriptionId = response.Result.SubscriptionId;

                var sharedEntity = new SharedEntity<TDto>(
                    this,
                    subscriptionId,
                    null,
                    query,
                    variables,
                    raw =>
                    {
                        var json = raw is string str ? str : JsonConvert.SerializeObject(raw);
                        var entity = JsonConvert.DeserializeObject<TEntity>(json);
                        return map(entity);
                    });

                return sharedEntity;
            }
            finally
            {
                subscription?.Dispose();
            }
        }

        private static DataSubscriptionException MapErrorToException(DataSubscriptionError error)
        {
            return error.ErrorCode switch
            {
                UpdateDataCorruptionException.Code => new UpdateDataCorruptionException(error.Message),
                SubscriptionTerminatedException.Code => new SubscriptionTerminatedException(0, error.Message),
                SubscriptionNotFoundException.Code => new SubscriptionNotFoundException(0, error.Message),
                _ => new DataSubscriptionException(error.ErrorCode, error.Message)
            };
        }
    }
}
