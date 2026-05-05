#if UNITY_EDITOR && !PLAYSERV_DISABLE_EDITOR_DTO_CODEGEN
using UnityEditor;
using UnityEngine;
using Playserv.CodeGenerator.Editor;

namespace Playserv.Editor
{
    internal sealed class PlayServCodegenSectionPresenter
    {
        public void Draw(PlayServWindowContext context)
        {
            var state = context.State;
            var expanded = PlayServWindowChrome.BeginSectionCard(
                ref state.FoldCodegen,
                "Automation",
                "Code Generation",
                "Generate DTOs on demand and keep the generated layer clean when you need a reset.");

            if (expanded)
            {
                bool autoGen = EditorPrefs.GetBool(Const.PrefKeyAutoCodegen, true);
                bool newAutoGen = EditorGUILayout.ToggleLeft("Enable automatic DTO generation", autoGen);

                if (newAutoGen != autoGen)
                    EditorPrefs.SetBool(Const.PrefKeyAutoCodegen, newAutoGen);

                GUILayout.Space(6f);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (PlayServWindowChrome.DrawActionButton("Generate DTOs Now", PlayServWindowButtonTone.Primary, GUILayout.Width(168f), GUILayout.Height(32f)))
                        SharedCodeGenerator.GenerateMenu();

                    GUILayout.Space(8f);

                    if (PlayServWindowChrome.DrawActionButton("Remove Generated DTOs", PlayServWindowButtonTone.Danger, GUILayout.Width(184f), GUILayout.Height(32f)))
                    {
                        if (EditorUtility.DisplayDialog(
                                "Remove DTOs",
                                "This will delete all generated DTO files.\nAre you sure?",
                                "Remove",
                                "Cancel"))
                        {
                            SharedCodeGenerator.DestroyDTOs();
                        }
                    }
                }
            }

            PlayServWindowChrome.EndSectionCard(expanded);
            EditorPrefs.SetBool(Const.PrefFoldCodegen, state.FoldCodegen);
        }
    }
}
#endif
