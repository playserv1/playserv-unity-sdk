using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Playserv.Events.Requests;
using Playserv.Events.Responses;
using Playserv.Proxy;
using Playserv.Proxy.Interfaces;
using Playserv.Proxy.Logging;

namespace Playserv.Events
{
    internal sealed class PlayServEventsAdapter : IEventsAdapter
    {
        private readonly ITransport _transport;
        private readonly ILogger _logger;
        private readonly EventSubscriptionManager _subscriptionManager;
        private readonly Dictionary<string, Type> _typeCache = new Dictionary<string, Type>();
        private readonly HashSet<string> _suppressedInfrastructureEventTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly object _typeCacheLock = new object();

        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            // Tweak if needed
            NullValueHandling = NullValueHandling.Include,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            DateParseHandling = DateParseHandling.DateTime,
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
        };

        static PlayServEventsAdapter()
        {
#if UNITY_5_3_OR_NEWER
            JsonSettings.Converters.Add(new Vector3JsonConverter());
            JsonSettings.Converters.Add(new QuaternionJsonConverter());
#endif
        }

        public PlayServEventsAdapter(ITransport transport, ILogger logger)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _subscriptionManager = new EventSubscriptionManager();

            SetupEventHandlers();
        }

        public IObservable<T> Subscribe<T>()
        {
            var eventType = typeof(T).Name;
            return new EventObservable<T>(_transport, _subscriptionManager, eventType, _logger);
        }

        public IDisposable Subscribe<T>(Action<T> onNext)
        {
            if (onNext == null)
                throw new ArgumentNullException(nameof(onNext));

            return Subscribe<T>().Subscribe(onNext);
        }

        public void Publish<T>(T @event)
        {
            if (@event == null)
                throw new ArgumentNullException(nameof(@event));

            var payload = JsonConvert.SerializeObject(@event, JsonSettings);
            var eventType = @event.GetType().Name;

            var message = new EventMessage(eventType, payload);
            _ = _transport.Send(message);
        }

        public void PublishForGroup<T>(string groupName, T @event)
        {
            if (string.IsNullOrWhiteSpace(groupName))
                throw new ArgumentException("Group name is required.", nameof(groupName));

            if (@event == null)
                throw new ArgumentNullException(nameof(@event));

            var payload = JsonConvert.SerializeObject(@event, JsonSettings);
            var eventType = @event.GetType().Name;

            var message = new GroupEventMessage(groupName, eventType, payload);
            _ = _transport.Send(message);
        }

        public void PublishForUser<T>(string userId, T @event)
        {
            if (string.IsNullOrWhiteSpace(userId))
                throw new ArgumentException("User id is required.", nameof(userId));

            if (@event == null)
                throw new ArgumentNullException(nameof(@event));

            var payload = JsonConvert.SerializeObject(@event, JsonSettings);
            var eventType = @event.GetType().Name;

            var message = new UserEventMessage(userId, eventType, payload);
            _ = _transport.Send(message);
        }

        private void SetupEventHandlers()
        {
            _transport.OnReceive<EventSubscribeResponse>()
                .Subscribe(response =>
                {
                    if (response == null || string.IsNullOrWhiteSpace(response.eventSubscriptionId))
                    {
                        _logger.LogWarning("Received EventSubscribeResponse with empty subscription id.");
                        return;
                    }

                    var existingTopic = _subscriptionManager.GetTopic(response.eventSubscriptionId);
                    if (!string.IsNullOrWhiteSpace(existingTopic))
                    {
                        _logger.LogWarning(
                            $"Ignoring duplicate subscription response: eventType={existingTopic}, subscriptionId={response.eventSubscriptionId}");
                        return;
                    }

                    var eventType = _subscriptionManager.GetFirstPendingTopic();
                    if (eventType == null)
                    {
                        if (_subscriptionManager.TryBindOrphanSubscriptionToSingleObservedTopic(
                                response.eventSubscriptionId,
                                out var reboundTopic))
                        {
                            _logger.LogWarning(
                                $"Recovered late subscription response: eventType={reboundTopic}, subscriptionId={response.eventSubscriptionId}");
                            return;
                        }

                        _logger.LogWarning(
                            $"Ignoring late subscription response without pending topic: subscriptionId={response.eventSubscriptionId}");
                        return;
                    }

                    _subscriptionManager.RemovePendingSubscription(eventType);
                    _subscriptionManager.AddSubscription(eventType, response.eventSubscriptionId);
                    _logger.Log($"Event subscription successful: eventType={eventType}, subscriptionId={response.eventSubscriptionId}");
                });

            _transport.OnReceive<ErrorResponse>()
                .Subscribe(error =>
                {
                    _logger.LogError($"SDK error: code={error.ErrorCode}, message={error.Message}");
                });

            _transport.OnReceive<EventUnsubscribeResponse>()
                .Subscribe(response =>
                {
                    if (response.success)
                        _logger.Log("Event unsubscription successful");
                    else
                        _logger.LogError("Event unsubscription failed");
                });

            _transport.OnReceive<EventMessage>()
                .Subscribe(HandleEventMessage);
        }

        private void HandleEventMessage(EventMessage message)
        {
            if (message == null || string.IsNullOrWhiteSpace(message.EventType))
                return;

            var eventType = FindTypeByName(message.EventType);
            if (eventType == null)
            {
                if (IsInfrastructureEventType(message.EventType))
                {
                    var shouldLogOnce = false;
                    lock (_typeCacheLock)
                    {
                        shouldLogOnce = _suppressedInfrastructureEventTypes.Add(message.EventType);
                    }

                    if (shouldLogOnce)
                        _logger.Log($"Ignoring infrastructure event type: {message.EventType}");

                    return;
                }

                _logger.LogError($"No event type found for type name: {message.EventType}");
                return;
            }

            try
            {
                var eventInstance = JsonConvert.DeserializeObject(message.Payload, eventType, JsonSettings);
                if (eventInstance == null)
                {
                    _logger.LogError($"Failed to deserialize event payload for type: {eventType.Name}");
                    return;
                }

                var notifyMethod = typeof(EventSubscriptionManager)
                    .GetMethod(nameof(EventSubscriptionManager.NotifyEvent));

                if (notifyMethod == null)
                {
                    _logger.LogError("NotifyEvent method not found on EventSubscriptionManager.");
                    return;
                }

                var genericNotify = notifyMethod.MakeGenericMethod(eventType);
                genericNotify.Invoke(_subscriptionManager, new object[] { eventInstance });
            }
            catch (JsonException ex)
            {
                _logger.LogError($"JSON deserialize error for EventMessage type={eventType.Name}: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error handling EventMessage type={eventType.Name}: {ex}");
            }
        }

        private Type FindTypeByName(string typeName)
        {
            lock (_typeCacheLock)
            {
                if (_typeCache.TryGetValue(typeName, out var cachedType))
                    return cachedType;
            }

            Type foundType = null;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                // Fast path: full name match
                var type = assembly.GetType(typeName);
                if (type != null)
                {
                    foundType = type;
                    break;
                }

                // Fallback: by simple name
                foreach (var t in assembly.GetTypes())
                {
                    if (t.Name == typeName)
                    {
                        foundType = t;
                        break;
                    }
                }

                if (foundType != null)
                    break;
            }

            lock (_typeCacheLock)
            {
                if (!_typeCache.ContainsKey(typeName))
                    _typeCache[typeName] = foundType;
            }

            return foundType;
        }

        private static bool IsInfrastructureEventType(string eventTypeName)
        {
            if (string.IsNullOrWhiteSpace(eventTypeName))
                return false;

            return string.Equals(eventTypeName, "KeepAlive", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(eventTypeName, "Heartbeat", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(eventTypeName, "Ping", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(eventTypeName, "Pong", StringComparison.OrdinalIgnoreCase);
        }
    }
}
