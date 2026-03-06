using System;
using Playserv.Proxy.Common;

namespace Playserv.DataSubscription
{
    /// <summary>
    /// Extension helpers for creating subscription builders from <see cref="PlayServImplementation"/>.
    /// </summary>
    public static class DataSubscriptionExtensions
    {
        /// <summary>
        /// Creates shared entity builder for type <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">Root entity type.</typeparam>
        /// <param name="proxy">Connected PlayServ implementation.</param>
        /// <returns>Fluent shared entity builder.</returns>
        public static ISharedEntityBuilder<T> Subscribe<T>(this PlayServImplementation proxy) where T : class, new()
        {
            if (proxy == null)
                throw new ArgumentNullException(nameof(proxy));

            var adapter = proxy.GetDataSubscriptionAdapter();
            return new SharedEntityBuilder<T>(adapter, typeof(T).Name);
        }
    }
}
