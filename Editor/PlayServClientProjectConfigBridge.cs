using Playserv.Wrapper;

namespace Playserv.Editor
{
    internal static class PlayServClientProjectConfigBridge
    {
        public static bool TryDraw(out bool changed)
        {
            return PlayServClientProjectSettingsRegistry.TryDrawProjectConfigUi(out changed);
        }
    }
}
