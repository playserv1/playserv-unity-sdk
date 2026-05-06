using System;
using Playserv.Proxy.Implementation;
using Playserv.Proxy.Interfaces;
using Playserv.Serialization;

namespace Playserv.Proxy.Common
{
    public static class PlayServJsonCompositionRoot
    {
        public static IMessageSerializer CreateDefaultSerializer()
        {
            var jsonCodec = CreateDefaultJsonCodec();
            return new JsonSerializer(jsonCodec, new NewtonsoftCommandPayloadMapper());
        }

        public static IJsonCodec CreateDefaultJsonCodec()
        {
            return new NewtonsoftJsonCodec();
        }

        internal static PlayServJsonComposition Resolve(IMessageSerializer serializer)
        {
            if (serializer == null)
                throw new ArgumentNullException(nameof(serializer));

            return new PlayServJsonComposition(
                serializer,
                ResolveCodec(serializer));
        }

        private static IJsonCodec ResolveCodec(IMessageSerializer serializer)
        {
            return serializer is JsonSerializer jsonSerializer
                ? jsonSerializer.JsonCodec
                : CreateDefaultJsonCodec();
        }
    }

    internal readonly struct PlayServJsonComposition
    {
        public PlayServJsonComposition(IMessageSerializer serializer, IJsonCodec jsonCodec)
        {
            Serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            JsonCodec = jsonCodec ?? throw new ArgumentNullException(nameof(jsonCodec));
        }

        public IMessageSerializer Serializer { get; }

        public IJsonCodec JsonCodec { get; }
    }
}
