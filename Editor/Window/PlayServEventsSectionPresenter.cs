#if UNITY_EDITOR && !PLAYSERV_DISABLE_EVENTS
using UnityEditor;

namespace Playserv.Editor
{
    internal sealed class PlayServEventsSectionPresenter
    {
        public void Draw(PlayServWindowContext context)
        {
            var state = context.State;
            var expanded = PlayServWindowChrome.BeginSectionCard(
                ref state.FoldEvents,
                "Realtime",
                "Events",
                "Generate the typed events API and keep event payload contracts close to the runtime.");

            PlayServWindowChrome.EndSectionCard(expanded);
            EditorPrefs.SetBool(Const.PrefFoldEvents, state.FoldEvents);
        }
    }
}
#endif
