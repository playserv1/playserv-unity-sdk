#if UNITY_5_3_OR_NEWER
using System.Collections.Generic;
using Playserv.Http.Interfaces;

namespace Playserv.Http.Common
{
    internal static partial class PlayServHttpModuleRegistry
    {
        static partial void RegisterUnity(List<IPlayServHttpModuleFactory> factories)
        {
            factories.Add(new Playserv.Http.Modules.Unity.UnityWebRequestHttpModuleFactory());
        }
    }
}
#endif
