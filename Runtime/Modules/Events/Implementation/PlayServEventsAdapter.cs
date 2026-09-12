using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Events.Requests;
using Playserv.Events.Responses;
using Playserv.Proxy;
using Playserv.Proxy.Common;
using Playserv.Proxy.Interfaces;
using Playserv.Proxy.Logging;
using Playserv.Serialization;

namespace Playserv.Events
{
    internal sealed class PlayServEventsAdapter : IEventsAdapter
    {
        private const string EventsModuleName = "module_events";

        private readonly ITransport _transport;
        private readonly IJsonCodec _jsonCodec;
        private readonly ILogger _logger;
        private readonly EventSubscriptionManager _subscriptionManager;
        private readonly EventTypeRegistry _eventTypeRegistry = new EventTypeRegistry();
        private readonly Dictionary<string, Func<string, object>> _eventPayloadDeserializers =
            new Dictionary<string, Func<string, object>>(StringComparer.Ordinal);
        private readonly HashSet<string> _suppressedInfrastructureEventTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly object _eventPayloadDeserializerLock = new object();
        private readonly object _infrastructureEventLock = new object();
        // Groups this client INTENDS to be in (added on subscribe call, removed on unsubscribe call,
        // regardless of response outcome). Server-side group membership is connection-scoped, so a
        // transparent reconnect silently drops it — this set is what gets replayed afterwards.
        private readonly HashSet<string> _activeGroups = new HashSet<string>(StringComparer.Ordinal);
        private readonly object _activeGroupsLock = new object();
        private readonly SemaphoreSlim _groupCommandGate = new SemaphoreSlim(1, 1);
        private static readonly TimeSpan GroupCommandTimeout = TimeSpan.FromSeconds(10);
        private static readonly JsonCodecOptions EventJsonOptions = UnityJsonCodecOptionsFactory.CreateDefaultEventOptions();

        public PlayServEventsAdapter(ITransport transport, IJsonCodec jsonCodec, ILogger logger)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _jsonCodec = jsonCodec ?? throw new ArgumentNullException(nameof(jsonCodec));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _subscriptionManager = new EventSubscriptionManager();

            SetupEventHandlers();
        }

        public IObservable<T> Subscribe<T>()
        {
            var eventClrType = typeof(T);
            _eventTypeRegistry.Register(eventClrType);
            var eventType = EventTypeRegistry.GetCanonicalName(eventClrType);
            RegisterEventPayloadDeserializer<T>(eventType);
            return new EventObservable<T>(_transport, _subscriptionManager, eventType, _logger);
        }

        public IObservable<string> SubscribeRaw<T>()
        {
            var eventClrType = typeof(T);
            _eventTypeRegistry.Register(eventClrType);
            var eventType = EventTypeRegistry.GetCanonicalName(eventClrType);
            return new RawEventObservable<T>(_transport, _subscriptionManager, eventType, _logger);
        }

        public IDisposable Subscribe<T>(Action<T> onNext)
        {
            if (onNext == null)
                throw new ArgumentNullException(nameof(onNext));

            return Subscribe<T>().Subscribe(onNext);
        }

        public IDisposable SubscribeRaw<T>(Action<string> onNext)
        {
            if (onNext == null)
                throw new ArgumentNullException(nameof(onNext));

            return SubscribeRaw<T>().Subscribe(onNext);
        }

        public void Publish<T>(T @event)
        {
            if (@event == null)
                throw new ArgumentNullException(nameof(@event));
            throw PlayServEventPublishingException.Broadcast();
        }

        public void PublishForGroup<T>(string groupName, T @event)
        {
            if (string.IsNullOrWhiteSpace(groupName))
                throw new ArgumentException("Group name is required.", nameof(groupName));

            if (@event == null)
                throw new ArgumentNullException(nameof(@event));
            throw PlayServEventPublishingException.Group(groupName.Trim());
        }

        public void PublishForUser<T>(string userId, T @event)
        {
            if (string.IsNullOrWhiteSpace(userId))
                throw new ArgumentException("User id is required.", nameof(userId));

            if (@event == null)
                throw new ArgumentNullException(nameof(@event));
            throw PlayServEventPublishingException.User(userId.Trim());
        }

        public Task<bool> SubscribeGroupAsync(string groupName, CancellationToken ct = default)
        {
            return ExecuteGroupCommandAsync<SubscribeGroupRequest, SubscribeGroupResponse>(
                new SubscribeGroupRequest(groupName),
                groupName,
                "subscribe",
                response => response.GroupName,
                response => response.success,
                response => response.errorCode,
                response => response.errorMessage,
                () =>
                {
                    lock (_activeGroupsLock)
                        _activeGroups.Add(groupName);
                },
                ct);
        }

        public Task<bool> UnsubscribeGroupAsync(string groupName, CancellationToken ct = default)
        {
            return ExecuteGroupCommandAsync<UnsubscribeGroupRequest, UnsubscribeGroupResponse>(
                new UnsubscribeGroupRequest(groupName),
                groupName,
                "unsubscribe",
                response => response.GroupName,
                response => response.success,
                response => response.errorCode,
                response => response.errorMessage,
                () =>
                {
                    lock (_activeGroupsLock)
                        _activeGroups.Remove(groupName);
                },
                ct);
        }

        /// <summary>
        /// Replays every event-type and group subscription onto the CURRENT connection. Server-side
        /// subscriptions are connection-scoped: a transparent reconnect gets a fresh connection whose
        /// registry entries are empty, so without this replay group broadcasts stay dead forever
        /// while RPC traffic keeps working (rpc responses never consult the group registry).
        /// Safe to call on the first connect too — nothing is tracked yet, so it is a no-op.
        /// </summary>
        public void ResubscribeAllOnReconnect()
        {
            var topics = _subscriptionManager.GetTopicsForResubscribe();
            _subscriptionManager.ResetConnectionSubscriptions();
            foreach (var eventType in topics)
            {
                _subscriptionManager.AddPendingSubscription(eventType);
                _ = _transport.Send(new EventSubscribeRequest(eventType), EventsModuleName);
                _logger.Log($"Replayed event subscription after reconnect: eventType={eventType}");
            }

            string[] groups;
            lock (_activeGroupsLock)
            {
                groups = new string[_activeGroups.Count];
                _activeGroups.CopyTo(groups);
            }

            foreach (var groupName in groups)
                _ = ReplayGroupSubscriptionAsync(groupName);
        }

        private async Task ReplayGroupSubscriptionAsync(string groupName)
        {
            try
            {
                var success = await SubscribeGroupAsync(groupName).ConfigureAwait(false);
                if (!success)
                    _logger.LogWarning($"Group resubscribe after reconnect was rejected: groupName={groupName}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Group resubscribe after reconnect failed: groupName={groupName}, error={ex.Message}");
            }
        }

        private void SetupEventHandlers()
        {
            _transport.OnReceive<EventSubscribeResponse>()
                .Subscribe(response =>
                {
                    if (response == null || string.IsNullOrWhiteSpace(response.EventType))
                    {
                        _logger.LogWarning("Received EventSubscribeResponse with empty EventType.");
                        return;
                    }

                    var eventType = response.EventType.Trim();
                    if (!_subscriptionManager.IsSubscriptionPending(eventType))
                    {
                        _logger.LogWarning(
                            $"Ignoring late or duplicate event subscription response: eventType={eventType}");
                        return;
                    }

                    if (!response.success)
                    {
                        var exception = new PlayServEventSubscriptionException(
                            eventType,
                            response.errorCode,
                            response.errorMessage);
                        _subscriptionManager.NotifySubscriptionError(eventType, exception);
                        _logger.LogError(
                            $"Event subscription rejected: eventType={eventType}, code={response.errorCode}");
                        return;
                    }

                    _subscriptionManager.MarkSubscriptionActive(eventType);
                    _logger.Log($"Event subscription successful: eventType={eventType}");
                });

            _transport.OnReceive<Playserv.Proxy.Common.ErrorResponse>()
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

            HandleEventPayload(message.EventType, message.Payload);
        }

        private void HandleEventPayload(string eventTypeName, string payload)
        {
            if (string.IsNullOrWhiteSpace(eventTypeName))
                return;

            var eventType = FindTypeByName(eventTypeName);
            if (eventType == null)
            {
                if (IsInfrastructureEventType(eventTypeName))
                {
                    var shouldLogOnce = false;
                    lock (_infrastructureEventLock)
                    {
                        shouldLogOnce = _suppressedInfrastructureEventTypes.Add(eventTypeName);
                    }

                    if (shouldLogOnce)
                        _logger.Log($"Ignoring infrastructure event type: {eventTypeName}");

                    return;
                }

                _logger.LogError($"No event type found for type name: {eventTypeName}");
                return;
            }

            try
            {
                _subscriptionManager.NotifyRawEvent(eventTypeName, payload);

                if (!_subscriptionManager.HasTypedObservers(eventType))
                    return;

                var eventInstance = DeserializeEventPayload(eventTypeName, payload, eventType);
                if (eventInstance == null)
                {
                    _logger.LogError($"Failed to deserialize event payload for type: {eventType.Name}");
                    return;
                }

                _subscriptionManager.NotifyEvent(eventType, eventInstance);
            }
            catch (JsonCodecException ex)
            {
                _logger.LogError($"JSON deserialize error for EventMessage type={eventType.Name}: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error handling EventMessage type={eventType.Name}: {ex}");
            }
        }

        private void RegisterEventPayloadDeserializer<T>(string eventTypeName)
        {
            lock (_eventPayloadDeserializerLock)
            {
                _eventPayloadDeserializers[eventTypeName] = payload => _jsonCodec.Deserialize<T>(payload, EventJsonOptions);
            }
        }

        private object DeserializeEventPayload(string eventTypeName, string payload, Type fallbackType)
        {
            Func<string, object> deserializer;
            lock (_eventPayloadDeserializerLock)
            {
                _eventPayloadDeserializers.TryGetValue(eventTypeName, out deserializer);
            }

            return deserializer != null
                ? deserializer(payload)
                : _jsonCodec.Deserialize(payload, fallbackType, EventJsonOptions);
        }

        private Type FindTypeByName(string typeName)
        {
            return _eventTypeRegistry.TryResolve(typeName, out var eventType)
                ? eventType
                : null;
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

        private async Task<bool> ExecuteGroupCommandAsync<TRequest, TResponse>(
            TRequest request,
            string groupName,
            string operationName,
            Func<TResponse, string> getGroupName,
            Func<TResponse, bool> getSuccess,
            Func<TResponse, int> getErrorCode,
            Func<TResponse, string> getErrorMessage,
            Action onSuccess,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(groupName))
                throw new ArgumentException("Group name is required.", nameof(groupName));

            if (request == null)
                throw new ArgumentNullException(nameof(request));

            if (getGroupName == null) throw new ArgumentNullException(nameof(getGroupName));
            if (getSuccess == null) throw new ArgumentNullException(nameof(getSuccess));
            if (getErrorCode == null) throw new ArgumentNullException(nameof(getErrorCode));
            if (getErrorMessage == null) throw new ArgumentNullException(nameof(getErrorMessage));
            if (onSuccess == null) throw new ArgumentNullException(nameof(onSuccess));

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(GroupCommandTimeout);

            await _groupCommandGate.WaitAsync(timeoutCts.Token);
            try
            {
                var completion = new TaskCompletionSource<GroupCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);

                using var responseSubscription = _transport.OnReceive<TResponse>().Subscribe(
                    response =>
                    {
                        var responseGroup = getGroupName(response);
                        if (!string.IsNullOrWhiteSpace(responseGroup) &&
                            !string.Equals(responseGroup, groupName, StringComparison.Ordinal))
                            return;

                        completion.TrySetResult(GroupCommandResult.FromResponse(
                            getSuccess(response),
                            getErrorCode(response),
                            getErrorMessage(response)));
                    },
                    ex => completion.TrySetException(ex),
                    () => completion.TrySetException(
                        new InvalidOperationException($"Transport completed while waiting for {typeof(TResponse).Name}.")));

                using var cancellationRegistration =
                    timeoutCts.Token.Register(() => completion.TrySetCanceled(timeoutCts.Token));

                await _transport.Send(request, EventsModuleName);
                _logger.Log($"Sent group {operationName} request: groupName={groupName}");

                var result = await completion.Task;
                if (result.Success)
                {
                    onSuccess();
                    _logger.Log($"Group {operationName} successful: groupName={groupName}");
                    return true;
                }

                throw new PlayServGroupSubscriptionException(
                    groupName,
                    operationName,
                    result.ErrorCode,
                    result.ErrorMessage);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"Timed out waiting for {typeof(TResponse).Name} for group '{groupName}'. " +
                    "Uncorrelated events-module errors are logged separately and are not treated as a response to this command.");
            }
            finally
            {
                _groupCommandGate.Release();
            }
        }

        private sealed class GroupCommandResult
        {
            public bool Success { get; }
            public int ErrorCode { get; }
            public string ErrorMessage { get; }

            private GroupCommandResult(bool success, int errorCode, string errorMessage)
            {
                Success = success;
                ErrorCode = errorCode;
                ErrorMessage = errorMessage;
            }

            public static GroupCommandResult FromResponse(bool success, int errorCode, string errorMessage) =>
                new GroupCommandResult(success, errorCode, errorMessage);

        }
    }
}
