using Playserv.Proxy.Common;
using Playserv.Proxy.Logging;

namespace Playserv.DataSubscription
{
    public static class DataSubscriptionExtensions
    {
        public static ISharedEntityBuilder<T> Subscribe<T>(this PlayServImplementation proxy) where T : class, new()
        {
            var logger = proxy.GetLogger();
            var adapter = new PlayServDataSubscriptionAdapter(proxy, logger);
            return new SharedEntityBuilder<T>(adapter, typeof(T).Name);
        }
    }
}
