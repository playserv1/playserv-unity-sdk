using System;
namespace Playserv.Wrapper
{
    public sealed class PlayServBrowserLoginOptions
    {
        public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(5);
        public bool AllowPlayerSwitch { get; set; }
    }
}
