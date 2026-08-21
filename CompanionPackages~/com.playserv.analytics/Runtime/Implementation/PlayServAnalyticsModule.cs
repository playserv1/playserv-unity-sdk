using System;
using Playserv.Modules;
using Playserv.Proxy.Common;
using Playserv.Proxy.Logging;
using UnityEngine;

[assembly: UnityEngine.Scripting.AlwaysLinkAssembly]
[assembly: PlayServModule(
    PlayServModuleManifest.AnalyticsId,
    typeof(Playserv.Analytics.PlayServAnalyticsModule),
    75)]

namespace Playserv.Analytics
{
    public sealed class PlayServAnalyticsModule :
        IPlayServModule,
        IPlayServConnectionAwareModule
    {
        private PlayServAnalyticsClient _client;

        public PlayServModuleDescriptor Descriptor { get; } =
            new PlayServModuleDescriptor(
                PlayServModuleIds.Analytics,
                isCore: false,
                PlayServModuleIds.Transport,
                PlayServModuleIds.Serialization);

        public void Initialize(PlayServModuleContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            context.Services.TryGet<IPlayServRuntimeIdentity>(out var runtimeIdentity);
            var provider = new PlayServAnalyticsProviderSelector(
                PlayServHttpAnalyticsProvider.CreateDefault());
            _client = new PlayServAnalyticsClient(
                provider,
                () => runtimeIdentity == null ? string.Empty : runtimeIdentity.UserId,
                PlayServLog.ForCategory(PlayServLogCategory.Analytics),
                SdkInfo.Version,
                Application.version,
                Application.platform.ToString());

            context.Services.Register<IPlayServAnalyticsClient>(_client);
            context.Services.Register(_client);
            context.Services.Register(this);
        }

        public void OnConnected()
        {
            _client?.NotifyConnected();
        }

        public void Shutdown()
        {
            _client?.Dispose();
            _client = null;
        }
    }

    internal static class PlayServAnalyticsModuleRegistration
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            PlayServModuleRegistry.Register<PlayServAnalyticsModule>(
                PlayServModuleManifest.AnalyticsId,
                75);
        }
    }
}
