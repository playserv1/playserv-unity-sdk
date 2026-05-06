using Playserv.Proxy.Interfaces;
using Playserv.Serialization;

namespace Playserv.Proxy.Common
{
    public delegate ITransportImplementation PlayServTransportImplementationFactory(
        string endpoint,
        IJsonCodec jsonCodec);
}
