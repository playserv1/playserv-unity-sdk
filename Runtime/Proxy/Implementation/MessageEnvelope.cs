using System;

namespace Playserv.Proxy.Implementation
{
    /// <summary>
    /// Transport-level envelope containing command name and serialized payload.
    /// </summary>
    [Serializable]
    public struct MessageEnvelope
    {
        /// <summary>
        /// Serialized payload JSON.
        /// </summary>
        public string Payload;

        /// <summary>
        /// Command name (optionally with module prefix).
        /// </summary>
        public string Command;

        /// <summary>
        /// Creates message envelope.
        /// </summary>
        /// <param name="command">Command name.</param>
        /// <param name="payloadJson">Serialized payload JSON.</param>
        public MessageEnvelope(string command, string payloadJson)
        {
            Payload = payloadJson;
            Command = command;
        }
    }
}
