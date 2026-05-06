using System;
using System.Collections.Generic;
using Playserv.Proxy.Logging;

namespace Playserv.Proxy.Common
{
    internal sealed class PlayServCommandRouter : IDisposable
    {
        private readonly PlayServFeatureFacade _featureFacade;
        private readonly PlayServTransportSession _transportSession;
        private readonly ILogger _logger;
        private readonly List<IDisposable> _subscriptions = new List<IDisposable>();

        public PlayServCommandRouter(
            PlayServFeatureFacade featureFacade,
            PlayServTransportSession transportSession,
            ILogger logger)
        {
            _featureFacade = featureFacade ?? throw new ArgumentNullException(nameof(featureFacade));
            _transportSession = transportSession ?? throw new ArgumentNullException(nameof(transportSession));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            Attach();
        }

        public void Dispose()
        {
            for (var i = 0; i < _subscriptions.Count; i++)
                _subscriptions[i].Dispose();

            _subscriptions.Clear();
        }

        private void Attach()
        {
            var routes = new PlayServCommandRouteRegistry(_transportSession, _logger, Register);
            var providers = CommandRouteProviderRegistry.Snapshot();
            for (var i = 0; i < providers.Length; i++)
                providers[i].RegisterRoutes(routes);
        }

        private void Register(string commandName, Action<object> onNext)
        {
            _subscriptions.Add(_featureFacade.OnCommand(commandName, onNext));
        }

    }
}
