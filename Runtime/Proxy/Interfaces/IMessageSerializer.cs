using Playserv.Proxy.Implementation;

namespace Playserv.Proxy.Interfaces
{
    public interface IMessageSerializer
    {
        MessageEnvelope Serialize<T>(T command, string moduleName = null);
        object Deserialize(MessageEnvelope envelope);
    }
}