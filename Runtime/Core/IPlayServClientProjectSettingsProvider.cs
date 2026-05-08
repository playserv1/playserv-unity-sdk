namespace Playserv.Wrapper
{
    public interface IPlayServClientProjectSettingsProvider
    {
        bool TryResolveSettings(PlayServConfig config, out PlayServSettings settings);

#if UNITY_EDITOR
        bool TryDrawProjectConfigUi(out bool changed);
#endif
    }
}
