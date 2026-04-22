using System;
using Playserv.Wrapper;

namespace Playserv.Http.Common
{
    internal sealed class PlayServHttpModuleContext
    {
        public PlayServHttpModuleContext(PlayServSettings settings)
        {
            Settings = settings?.Clone() ?? throw new ArgumentNullException(nameof(settings));
        }

        public PlayServSettings Settings { get; }
    }
}
