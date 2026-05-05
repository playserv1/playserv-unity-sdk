using System;

namespace Playserv.Modules
{
    public sealed class PlayServProvidedModule : IPlayServModule
    {
        public PlayServProvidedModule(string id, bool isCore = true)
        {
            Descriptor = new PlayServModuleDescriptor(id, isCore);
        }

        public PlayServModuleDescriptor Descriptor { get; }

        public void Initialize(PlayServModuleContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
        }

        public void Shutdown()
        {
        }
    }
}
