#if !PLAYSERV_DISABLE_PULSE
namespace Playserv.Pulse
{
    public interface IPlayServPulseModule
    {
        bool IsImplemented { get; }
    }
}

#endif
