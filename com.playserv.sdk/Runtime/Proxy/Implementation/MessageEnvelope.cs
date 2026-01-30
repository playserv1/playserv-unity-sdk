using System;

namespace Playserv.Proxy.Implementation
{
    [Serializable]
    public struct MessageEnvelope
    {
        public string Payload;
        public string Command;

        public MessageEnvelope(string command, string payloadJson)
        {
            Payload = payloadJson;
            Command = command;
        }
    }
}