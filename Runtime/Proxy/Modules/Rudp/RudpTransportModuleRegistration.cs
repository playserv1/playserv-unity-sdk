using Playserv.Proxy.Common;
using UnityEngine;

namespace Playserv.Proxy.Implementation
{
    internal static class RudpTransportModuleRegistration
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Register()
        {
            TransportModuleRegistry.Register(new RudpTransportModuleFactory());
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
