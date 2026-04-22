using Playserv.Runtime.Abstractions;

namespace Playserv.Proxy.Interfaces
{
    internal interface ITransportModuleFactory
    {
        string Scheme { get; }

        ITransportImplementation Create(TransportModuleContext context);
    }
}
