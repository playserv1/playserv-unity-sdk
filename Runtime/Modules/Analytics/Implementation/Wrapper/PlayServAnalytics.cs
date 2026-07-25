using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Analytics;
using Playserv.Modules;
using Playserv.Proxy.Common;

namespace Playserv.Wrapper
{
    public interface IPlayServAnalyticsApi
    {
        bool CollectionEnabled { get; }

        int PendingEventCount { get; }

        void SetCollectionEnabled(bool enabled);

        void SetProvider(IPlayServAnalyticsProvider provider);

        void SetUserId(string userId);

        void SetUserProperty(string key, string value);

        void RemoveUserProperty(string key);

        void ClearUserProperties();

        void Track(string eventName);

        void Track(string eventName, string key, object value);

        void Track(
            string eventName,
            IReadOnlyDictionary<string, object> parameters);

        Task FlushAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Gameplay analytics surface backed by the optional PlayServ Analytics module.
    /// </summary>
    public static class PlayServAnalytics
    {
        public static IPlayServAnalyticsApi Api { get; } =
            new PlayServApiAnalyticsFacade();

        public static bool CollectionEnabled => Api.CollectionEnabled;

        public static int PendingEventCount => Api.PendingEventCount;

        public static void SetCollectionEnabled(bool enabled) =>
            Api.SetCollectionEnabled(enabled);

        public static void SetProvider(IPlayServAnalyticsProvider provider) =>
            Api.SetProvider(provider);

        public static void SetUserId(string userId) => Api.SetUserId(userId);

        public static void SetUserProperty(string key, string value) =>
            Api.SetUserProperty(key, value);

        public static void RemoveUserProperty(string key) =>
            Api.RemoveUserProperty(key);

        public static void ClearUserProperties() => Api.ClearUserProperties();

        public static void Track(string eventName) => Api.Track(eventName);

        public static void Track(string eventName, string key, object value) =>
            Api.Track(eventName, key, value);

        public static void Track(
            string eventName,
            IReadOnlyDictionary<string, object> parameters) =>
            Api.Track(eventName, parameters);

        public static Task FlushAsync(
            CancellationToken cancellationToken = default) =>
            Api.FlushAsync(cancellationToken);
    }

    internal sealed class PlayServApiAnalyticsFacade : IPlayServAnalyticsApi
    {
        private readonly IPlayServAnalyticsRuntimeAccess _runtimeAccess;

        public PlayServApiAnalyticsFacade()
            : this(new PlayServAnalyticsRuntimeAccess())
        {
        }

        internal PlayServApiAnalyticsFacade(
            IPlayServAnalyticsRuntimeAccess runtimeAccess)
        {
            _runtimeAccess = runtimeAccess ??
                             throw new ArgumentNullException(nameof(runtimeAccess));
        }

        public bool CollectionEnabled =>
            TryGetCurrentClient(out var client) && client.CollectionEnabled;

        public int PendingEventCount =>
            TryGetCurrentClient(out var client) ? client.PendingEventCount : 0;

        public void SetCollectionEnabled(bool enabled)
        {
            GetClientForFireAndForget("analytics collection configuration")
                ?.SetCollectionEnabled(enabled);
        }

        public void SetProvider(IPlayServAnalyticsProvider provider)
        {
            if (provider == null)
                throw new ArgumentNullException(nameof(provider));

            RequiredClient.SetProvider(provider);
        }

        public void SetUserId(string userId)
        {
            GetClientForFireAndForget("analytics user configuration")
                ?.SetUserId(userId);
        }

        public void SetUserProperty(string key, string value)
        {
            GetClientForFireAndForget("analytics user property update")
                ?.SetUserProperty(key, value);
        }

        public void RemoveUserProperty(string key)
        {
            GetClientForFireAndForget("analytics user property removal")
                ?.RemoveUserProperty(key);
        }

        public void ClearUserProperties()
        {
            GetClientForFireAndForget("analytics user property reset")
                ?.ClearUserProperties();
        }

        public void Track(string eventName)
        {
            Track(eventName, parameters: null);
        }

        public void Track(string eventName, string key, object value)
        {
            Track(
                eventName,
                new Dictionary<string, object>
                {
                    { key, value }
                });
        }

        public void Track(
            string eventName,
            IReadOnlyDictionary<string, object> parameters)
        {
            GetClientForFireAndForget("analytics event tracking")
                ?.Track(eventName, parameters);
        }

        public Task FlushAsync(CancellationToken cancellationToken = default)
        {
            return RequiredClient.FlushAsync(cancellationToken);
        }

        private IPlayServAnalyticsClient RequiredClient =>
            GetClient(_runtimeAccess.RequiredServices);

        private bool TryGetCurrentClient(out IPlayServAnalyticsClient client)
        {
            client = null;
            var services = _runtimeAccess.CurrentServices;
            return services != null &&
                   services.TryGet(out client);
        }

        private IPlayServAnalyticsClient GetClientForFireAndForget(
            string operationName)
        {
            var services = _runtimeAccess.GetServicesForFireAndForget(operationName);
            return services == null ? null : GetClient(services);
        }

        private static IPlayServAnalyticsClient GetClient(
            IPlayServModuleServiceProvider services)
        {
            if (services == null)
                throw new InvalidOperationException("SDK is not connected. Call Connect() first.");

            return services.Get<IPlayServAnalyticsClient>();
        }
    }

    internal interface IPlayServAnalyticsRuntimeAccess
    {
        IPlayServModuleServiceProvider CurrentServices { get; }

        IPlayServModuleServiceProvider RequiredServices { get; }

        IPlayServModuleServiceProvider GetServicesForFireAndForget(
            string operationName);
    }

    internal sealed class PlayServAnalyticsRuntimeAccess :
        IPlayServAnalyticsRuntimeAccess
    {
        public IPlayServModuleServiceProvider CurrentServices =>
            PlayServRuntimeHost.CurrentModuleServices;

        public IPlayServModuleServiceProvider RequiredServices =>
            PlayServRuntimeHost.RequiredModuleServices;

        public IPlayServModuleServiceProvider GetServicesForFireAndForget(
            string operationName) =>
            PlayServRuntimeHost.GetModuleServicesForFireAndForget(operationName);
    }
}
