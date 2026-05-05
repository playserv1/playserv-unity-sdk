using System;
using Playserv.Serialization;

namespace Playserv.Runtime.Abstractions
{
    internal sealed class PlayServHttpModuleContext
    {
        public PlayServHttpModuleContext(PlayServRuntimeSettings settings, IJsonCodec jsonCodec = null)
        {
            Settings = settings?.Clone() ?? throw new ArgumentNullException(nameof(settings));
            JsonCodec = jsonCodec;
        }

        public PlayServRuntimeSettings Settings { get; }

        public IJsonCodec JsonCodec { get; }
    }
}
