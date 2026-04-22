using Playserv.Proxy.Common;

namespace Playserv.Proxy.Interfaces
{
    internal interface ITransportModuleFactory
    {
        string Scheme { get; }

        ITransportImplementation Create(TransportModuleContext context);
    }
}
