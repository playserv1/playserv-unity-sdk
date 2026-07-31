using System;
using System.Threading;
using System.Threading.Tasks;

namespace Playserv.Analytics
{
    internal static class PlayServAnalyticsProviderRegistry
    {
        private static readonly object Gate = new object();
        private static IPlayServAnalyticsProvider _customProvider;

        public static bool HasCustomProvider
        {
            get
            {
                lock (Gate)
                    return _customProvider != null;
            }
        }

        public static IPlayServAnalyticsProvider Current
        {
            get
            {
                lock (Gate)
                    return _customProvider;
            }
        }

        public static void Set(IPlayServAnalyticsProvider provider)
        {
            if (provider == null)
                throw new ArgumentNullException(nameof(provider));

            lock (Gate)
                _customProvider = provider;
        }

        public static void Reset()
        {
            lock (Gate)
                _customProvider = null;
        }
    }

    internal sealed class PlayServAnalyticsProviderSelector :
        IPlayServAnalyticsProvider
    {
        private readonly IPlayServAnalyticsProvider _defaultProvider;

        public PlayServAnalyticsProviderSelector(
            IPlayServAnalyticsProvider defaultProvider)
        {
            _defaultProvider = defaultProvider ??
                               throw new ArgumentNullException(nameof(defaultProvider));
        }

        public bool IsReady => ResolveProvider().IsReady;

        public Task SendAsync(
            PlayServAnalyticsBatch batch,
            CancellationToken cancellationToken = default)
        {
            return ResolveProvider().SendAsync(batch, cancellationToken);
        }

        private IPlayServAnalyticsProvider ResolveProvider()
        {
            return PlayServAnalyticsProviderRegistry.Current ?? _defaultProvider;
        }
    }
}
