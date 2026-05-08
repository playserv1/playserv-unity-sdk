using UnityEditor;

namespace Playserv.Editor
{
    [InitializeOnLoad]
    internal static class PlayServModelEditorSectionRegistration
    {
        static PlayServModelEditorSectionRegistration()
        {
            PlayServEditorSectionRegistry.Register(
                PlayServEditorSectionIds.ModelSync,
                () => new PlayServModelSectionPresenter());
        }
    }
}
