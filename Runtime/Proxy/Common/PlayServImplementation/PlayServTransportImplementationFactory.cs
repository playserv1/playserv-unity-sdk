using Playserv.Proxy.Interfaces;
using Playserv.Serialization;

namespace Playserv.Proxy.Common
{
    internal delegate ITransportImplementation PlayServTransportImplementationFactory(
        string endpoint,
        IJsonCodec jsonCodec);
}
