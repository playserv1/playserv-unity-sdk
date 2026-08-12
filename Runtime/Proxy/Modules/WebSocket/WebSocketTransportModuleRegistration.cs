using Playserv.Proxy.Common;
using UnityEngine;

[assembly: UnityEngine.Scripting.AlwaysLinkAssembly]

namespace Playserv.Proxy.Implementation
{
    internal static class WebSocketTransportModuleRegistration
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Register()
        {
            TransportModuleRegistry.Register(new WebSocketTransportModuleFactory("http"));
            TransportModuleRegistry.Register(new WebSocketTransportModuleFactory("https"));
            TransportModuleRegistry.Register(new WebSocketTransportModuleFactory("ws"));
            TransportModuleRegistry.Register(new WebSocketTransportModuleFactory("wss"));
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void RegisterInEditor()
        {
            Register();
        }
#endif
    }
}
