using System;
using Playserv.Modules;

namespace Playserv.DataSubscription
{
    /// <summary>
    /// Extension helpers for creating subscription builders from module service providers.
    /// </summary>
    public static class DataSubscriptionExtensions
    {
        /// <summary>
        /// Creates shared entity builder for type <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">Root entity type.</typeparam>
        /// <param name="host">Module service host.</param>
        /// <param name="mode">Subscription backend mode. Defaults to Polling.</param>
        /// <returns>Fluent shared entity builder.</returns>
        public static ISharedEntityBuilder<T> Subscribe<T>(
            this IPlayServModuleServiceHost host,
            DataSubscriptionMode mode = DataSubscriptionMode.Polling) where T : class, new()
        {
            if (host == null)
                throw new ArgumentNullException(nameof(host));

            var adapter = host.ModuleServices.Get<PlayServDataSubscriptionAdapter>();
            var builder = new SharedEntityBuilder<T>(adapter, typeof(T).Name);
            return mode == DataSubscriptionMode.Transport
                ? builder.UseTransport()
                : builder.UsePolling();
        }
    }
}
