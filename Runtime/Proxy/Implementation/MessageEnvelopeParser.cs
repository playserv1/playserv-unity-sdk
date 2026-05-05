using System;
using Playserv.Serialization;

namespace Playserv.Proxy.Implementation
{
    internal static class MessageEnvelopeParser
    {
        public static MessageEnvelope Parse(string json, IJsonCodec jsonCodec)
        {
            if (jsonCodec == null)
                throw new ArgumentNullException(nameof(jsonCodec));

            return new MessageEnvelopeCodec(jsonCodec).Parse(json);
        }
    }
}
