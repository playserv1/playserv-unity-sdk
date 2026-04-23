using System;
using Playserv.DataSubscription;
using Playserv.Events;
using Playserv.Proxy.Interfaces;
using Playserv.Proxy.Logging;

namespace Playserv.Proxy.Common
{
    internal sealed class PlayServImplementationComponents
    {
        public PlayServImplementationComponents(
            ITransport transport,
            ILogger logger,
            PlayServEventsAdapter eventsAdapter,
            PlayServDataSubscriptionAdapter dataSubscriptionAdapter,
            PlayServTransportSession transportSession)
        {
            Transport = transport ?? throw new ArgumentNullException(nameof(transport));
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
            EventsAdapter = eventsAdapter ?? throw new ArgumentNullException(nameof(eventsAdapter));
            DataSubscriptionAdapter = dataSubscriptionAdapter ?? throw new ArgumentNullException(nameof(dataSubscriptionAdapter));
            TransportSession = transportSession ?? throw new ArgumentNullException(nameof(transportSession));
        }

        public ITransport Transport { get; }

        public ILogger Logger { get; }

        public PlayServEventsAdapter EventsAdapter { get; }

        public PlayServDataSubscriptionAdapter DataSubscriptionAdapter { get; }

        public PlayServTransportSession TransportSession { get; }
    }
}
