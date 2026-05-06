using Playserv.Runtime.Abstractions;

namespace Playserv.Http.Interfaces
{
    public interface IPlayServHttpModuleFactory
    {
        IPlayServRuntimeHttpClient Create(PlayServHttpModuleContext context);
    }
}
