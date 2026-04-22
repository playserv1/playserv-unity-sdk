using System.Collections.Generic;
using Playserv.Http.Interfaces;

namespace Playserv.Http.Common
{
    internal static partial class PlayServHttpModuleRegistry
    {
        internal static IPlayServHttpModuleFactory[] GetFactories()
        {
            var factories = new List<IPlayServHttpModuleFactory>();
            RegisterUnity(factories);
            return factories.ToArray();
        }

        static partial void RegisterUnity(List<IPlayServHttpModuleFactory> factories);
    }
}
