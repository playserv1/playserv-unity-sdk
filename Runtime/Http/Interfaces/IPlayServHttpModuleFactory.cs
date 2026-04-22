using Playserv.Runtime.Abstractions;

namespace Playserv.Http.Interfaces
{
    internal interface IPlayServHttpModuleFactory
    {
        IPlayServRuntimeHttpClient Create(PlayServHttpModuleContext context);
    }
}
