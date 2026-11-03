using UnityEditor;

namespace Playserv.Editor
{
    [InitializeOnLoad]
    internal static class PlayServDeploymentEditorSectionRegistration
    {
        static PlayServDeploymentEditorSectionRegistration()
        {
            PlayServEditorSectionRegistry.Register(
                PlayServEditorSectionIds.Deployment,
                () => new PlayServDeploymentWindowBridge());
        }
    }
}
