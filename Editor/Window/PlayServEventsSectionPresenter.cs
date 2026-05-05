#if UNITY_EDITOR && !PLAYSERV_DISABLE_EVENTS
using UnityEditor;
using UnityEngine;
using Playserv.Events.Editor;

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

            if (expanded)
            {
                if (PlayServWindowChrome.DrawActionButton("Generate Events API", PlayServWindowButtonTone.Primary, GUILayout.Width(168f), GUILayout.Height(32f)))
                    EventsCodeGenerator.Generate();
            }

            PlayServWindowChrome.EndSectionCard(expanded);
            EditorPrefs.SetBool(Const.PrefFoldEvents, state.FoldEvents);
        }
    }
}
#endif
