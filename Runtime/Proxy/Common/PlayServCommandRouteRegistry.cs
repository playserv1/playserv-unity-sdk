using System;
using Playserv.Proxy.Logging;

namespace Playserv.Proxy.Common
{
    public sealed class PlayServCommandRouteRegistry
    {
        private readonly Action<string, Action<object>> _register;

        internal PlayServCommandRouteRegistry(
            PlayServTransportSession transportSession,
            ILogger logger,
            Action<string, Action<object>> register)
        {
            TransportSession = transportSession ?? throw new ArgumentNullException(nameof(transportSession));
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _register = register ?? throw new ArgumentNullException(nameof(register));
        }

        internal PlayServTransportSession TransportSession { get; }

        internal ILogger Logger { get; }

        public void Register(string commandName, Action<object> onNext)
        {
            if (string.IsNullOrWhiteSpace(commandName))
                throw new ArgumentException("Command name cannot be null or empty.", nameof(commandName));

            if (onNext == null)
                throw new ArgumentNullException(nameof(onNext));

            _register(commandName, onNext);
        }
    }
}
