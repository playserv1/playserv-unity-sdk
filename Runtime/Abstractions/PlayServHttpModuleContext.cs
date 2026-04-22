using System;

namespace Playserv.Runtime.Abstractions
{
    internal sealed class PlayServHttpModuleContext
    {
        public PlayServHttpModuleContext(PlayServRuntimeSettings settings)
        {
            Settings = settings?.Clone() ?? throw new ArgumentNullException(nameof(settings));
        }

        public PlayServRuntimeSettings Settings { get; }
    }
}
