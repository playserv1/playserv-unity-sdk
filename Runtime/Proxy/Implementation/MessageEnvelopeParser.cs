using System;
using Playserv.Serialization;

namespace Playserv.Proxy.Implementation
{
    internal static class MessageEnvelopeParser
    {
        private static readonly MessageEnvelopeCodec Codec =
            new MessageEnvelopeCodec(new NewtonsoftJsonCodec());

        public static MessageEnvelope Parse(string json)
        {
            return Codec.Parse(json);
        }
    }
}
