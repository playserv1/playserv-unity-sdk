using Playserv.Modules;
using Playserv.Proxy.Common;
using UnityEngine;

[assembly: UnityEngine.Scripting.AlwaysLinkAssembly]
[assembly: PlayServModule(
    PlayServModuleManifest.ServerId,
    typeof(Playserv.Server.PlayServServerModule),
    50)]
[assembly: PlayServLocalExecutionFactory(
    typeof(Playserv.Server.PlayServServerLocalExecution),
    100)]

namespace Playserv.Server
{
    internal static class PlayServServerModuleRegistration
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            PlayServModuleRegistry.Register<PlayServServerModule>(
                PlayServModuleManifest.ServerId,
                50);
            PlayServLocalExecutionFactoryRegistry.Register(
                100,
                () => new PlayServServerLocalExecution());
        }
    }
}
