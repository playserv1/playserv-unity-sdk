using System;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Modules;
using Playserv.Proxy.Common;

namespace Playserv.Analytics
{
    internal sealed class PlayServCommandAnalyticsProvider : IPlayServAnalyticsProvider
    {
        public const string ModuleName = "module_analytics";

        private readonly IPlayServCommandBus _commandBus;

        public PlayServCommandAnalyticsProvider(IPlayServCommandBus commandBus)
        {
            _commandBus = commandBus ?? throw new ArgumentNullException(nameof(commandBus));
        }

        public bool IsReady => _commandBus.State == PlayServState.Online;

        public async Task SendAsync(
            PlayServAnalyticsBatch batch,
            CancellationToken cancellationToken = default)
        {
            if (batch == null)
                throw new ArgumentNullException(nameof(batch));

            cancellationToken.ThrowIfCancellationRequested();
            if (!IsReady)
                throw new InvalidOperationException("PlayServ analytics transport is not online.");

            await _commandBus.SendAsync(batch, ModuleName);
            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}
