#if UNITY_5_3_OR_NEWER
using Playserv.Http.Common;
using Playserv.Http.Interfaces;

namespace Playserv.Http.Modules.Unity
{
    internal sealed class UnityWebRequestHttpModuleFactory : IPlayServHttpModuleFactory
    {
        public IPlayServRuntimeHttpClient Create(PlayServHttpModuleContext context)
        {
            if (context?.Settings == null)
                return null;

            return new UnityWebRequestRuntimeHttpClient(context.Settings);
        }
    }
}
#endif
