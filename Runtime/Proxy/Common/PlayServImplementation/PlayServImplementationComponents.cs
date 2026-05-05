using System;
using Playserv.Modules;
using Playserv.Proxy.Interfaces;
using Playserv.Proxy.Logging;

namespace Playserv.Proxy.Common
{
    internal sealed class PlayServImplementationComponents
    {
        public PlayServImplementationComponents(
            ITransport transport,
            ILogger logger,
            PlayServModuleHost moduleHost,
            PlayServTransportSession transportSession)
        {
            Transport = transport ?? throw new ArgumentNullException(nameof(transport));
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
            ModuleHost = moduleHost ?? throw new ArgumentNullException(nameof(moduleHost));
            TransportSession = transportSession ?? throw new ArgumentNullException(nameof(transportSession));
        }

        public ITransport Transport { get; }

        public ILogger Logger { get; }

        public PlayServModuleHost ModuleHost { get; }

        public PlayServTransportSession TransportSession { get; }
    }
}
