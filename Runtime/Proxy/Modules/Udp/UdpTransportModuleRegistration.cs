using Playserv.Proxy.Common;
using UnityEngine;

namespace Playserv.Proxy.Implementation
{
    internal static class UdpTransportModuleRegistration
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Register()
        {
            TransportModuleRegistry.Register(new UdpTransportModuleFactory());
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
