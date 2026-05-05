using System;

namespace Playserv.Modules
{
    public sealed class PlayServModuleContext
    {
        public PlayServModuleContext(IPlayServModuleServiceRegistry services)
        {
            Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        public IPlayServModuleServiceRegistry Services { get; }
    }
}
