using UnityEditor;

namespace Playserv.Editor
{
    [InitializeOnLoad]
    internal static class PlayServCodegenEditorSectionRegistration
    {
        static PlayServCodegenEditorSectionRegistration()
        {
            PlayServEditorSectionRegistry.Register(
                PlayServEditorSectionIds.Codegen,
                () => new PlayServCodegenSectionPresenter());
        }
    }
}
