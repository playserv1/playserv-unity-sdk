#if UNITY_5_3_OR_NEWER
using Playserv.Http.Interfaces;
using Playserv.Runtime.Abstractions;

namespace Playserv.Http.Modules.Unity
{
    internal sealed class UnityWebRequestHttpModuleFactory : IPlayServHttpModuleFactory
    {
        public IPlayServRuntimeHttpClient Create(PlayServHttpModuleContext context)
        {
            if (context?.Settings == null)
                return null;

            return new UnityWebRequestRuntimeHttpClient(context.Settings, context.JsonCodec);
        }
    }
}
#endif
