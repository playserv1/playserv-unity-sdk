using System;

namespace Playserv.Serialization
{
    public interface ICommandPayloadMapper
    {
        string BuildKeepAlivePayloadJson(string eventType, string payloadJson);

        string NormalizePayloadForType(string payloadJson, Type type);

        bool TryDeserializeAsCommandError(string payloadJson, out CommandErrorResponse response);
    }
}
