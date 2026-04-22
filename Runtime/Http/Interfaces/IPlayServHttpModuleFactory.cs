using Playserv.Http.Common;

namespace Playserv.Http.Interfaces
{
    internal interface IPlayServHttpModuleFactory
    {
        IPlayServRuntimeHttpClient Create(PlayServHttpModuleContext context);
    }
}
