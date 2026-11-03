using Playserv.Events.Editor;
using UnityEditor;
using UnityEngine;

namespace Playserv.Editor
{
    internal sealed class PlayServEventsSectionPresenter : IPlayServEditorSection
    {
        public void Dispose()
        {
        }

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
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (PlayServWindowChrome.DrawActionButton("Generate Events API", PlayServWindowButtonTone.Primary, GUILayout.Width(168f), GUILayout.Height(32f)))
                        EventsCodeGenerator.Generate();

                    GUILayout.FlexibleSpace();
                }
            }

            PlayServWindowChrome.EndSectionCard(expanded);
            EditorPrefs.SetBool(Const.PrefFoldEvents, state.FoldEvents);
        }
    }
}
