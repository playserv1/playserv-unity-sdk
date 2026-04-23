using System;
using System.Threading;
using Playserv.DataSubscription;
using Playserv.Events;
using Playserv.Proxy.Implementation;
using Playserv.Proxy.Interfaces;
using Playserv.Proxy.Logging;
using Playserv.RPC;
using Playserv.Runtime.Abstractions;

namespace Playserv.Proxy.Common
{
    internal static class PlayServInstanceFactory
    {
        public static PlayServImplementationComponents Create(
            PlayServImplementation owner,
            string endpoint,
            IMessageSerializer serializer,
            IRequestIdGenerator requestIdGenerator,
            ILogger logger,
            Func<string, ITransportImplementation> transportImplementationFactory,
            SynchronizationContext mainThreadContext,
            Action<TransportError> notifyTransportError,
            Action notifyKeepAlivePingSent,
            Action notifyKeepAlivePongReceived,
            Action<InvokeRpcResponse> notifyRpcInvokeResponse,
            Action onConnected)
        {
            if (owner == null)
                throw new ArgumentNullException(nameof(owner));

            if (serializer == null)
                throw new ArgumentNullException(nameof(serializer));

            if (requestIdGenerator == null)
                throw new ArgumentNullException(nameof(requestIdGenerator));

            if (logger == null)
                throw new ArgumentNullException(nameof(logger));

            if (string.IsNullOrWhiteSpace(endpoint))
                throw new ArgumentException("Endpoint cannot be null or empty.", nameof(endpoint));

            if (transportImplementationFactory == null)
                transportImplementationFactory = CreateDefaultTransportImplementationFactory(logger);

            var implementation = transportImplementationFactory(endpoint);
            if (implementation == null)
                throw new InvalidOperationException("Transport implementation factory returned null.");

            var transport = new Transport(implementation, serializer, requestIdGenerator, logger);
            var eventsAdapter = new PlayServEventsAdapter(transport, PlayServLog.ForCategory(PlayServLogCategory.Events));
            var dataSubscriptionAdapter = new PlayServDataSubscriptionAdapter(owner, PlayServLog.ForCategory(PlayServLogCategory.Data));
            var transportSession = new PlayServTransportSession(
                transport,
                logger,
                mainThreadContext,
                notifyTransportError,
                notifyKeepAlivePingSent,
                notifyKeepAlivePongReceived,
                notifyRpcInvokeResponse,
                onConnected);

            return new PlayServImplementationComponents(
                transport,
                logger,
                eventsAdapter,
                dataSubscriptionAdapter,
                transportSession);
        }

        private static Func<string, ITransportImplementation> CreateDefaultTransportImplementationFactory(ILogger logger)
        {
            return endpoint => TransportImplementationResolver.Create(new TransportModuleContext(endpoint, logger));
        }
    }
}
