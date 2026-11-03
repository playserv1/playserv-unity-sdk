using UnityEditor;

namespace Playserv.Editor
{
    [InitializeOnLoad]
    internal static class PlayServEventsEditorSectionRegistration
    {
        static PlayServEventsEditorSectionRegistration()
        {
            PlayServEditorSectionRegistry.Register(
                PlayServEditorSectionIds.Events,
                () => new PlayServEventsSectionPresenter());
        }
    }
}
