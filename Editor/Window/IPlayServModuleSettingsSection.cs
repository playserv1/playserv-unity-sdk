namespace Playserv.Editor
{
    internal interface IPlayServModuleSettingsSection
    {
        int Order { get; }

        bool Draw(
            PlayServWindowContext context,
            PlayServEditorModuleSettings settings,
            bool hasRuntimeModules,
            bool hasServerRuntimeModules);
    }
}
