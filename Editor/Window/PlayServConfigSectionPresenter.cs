using System;
using UnityEditor;
using UnityEngine;
using Playserv.Wrapper;

namespace Playserv.Editor
{
    internal sealed class PlayServConfigSectionPresenter
    {
        public void Draw(PlayServWindowContext context)
        {
            var state = context.State;
            var expanded = PlayServWindowChrome.BeginSectionCard(
                ref state.FoldConfig,
                "Control Room",
                "PlayServ Config",
                "Manage runtime identity, fixed endpoints, SDK version, and the project-side config asset from one place.");

            if (expanded)
            {
                if (context.Config == null || context.SerializedObject == null)
                {
                    if (PlayServWindowChrome.DrawActionButton("Create / Locate Config", PlayServWindowButtonTone.Primary, GUILayout.Width(176f), GUILayout.Height(32f)))
                        context.EnsureConfig();

                    PlayServWindowChrome.DrawNotice("Config asset not found.", MessageType.Warning);
                }
                else
                {
                    bool configUiChanged;
                    if (PlayServClientProjectConfigBridge.TryDraw(out configUiChanged))
                    {
                        GUILayout.Space(6f);

                        if (configUiChanged)
                        {
                            context.EnsureConfig();
                            context.Repaint();
                        }
                    }

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.ObjectField("Config Asset", context.Config, typeof(PlayServConfig), false);
                    if (PlayServWindowChrome.DrawActionButton("Ping", PlayServWindowButtonTone.Secondary, GUILayout.Width(72f), GUILayout.Height(30f)))
                        EditorGUIUtility.PingObject(context.Config);
                    EditorGUILayout.EndHorizontal();

                    GUILayout.Space(6f);

                    context.SerializedObject.Update();

                    EditorGUILayout.PropertyField(context.ClientTokenProperty);
                    EditorGUILayout.PropertyField(context.AuthorizationProperty);
                    EditorGUILayout.PropertyField(context.GameIdProperty);
                    EditorGUILayout.PropertyField(context.GameVersionProperty);
                    EditorGUILayout.PropertyField(context.AllowMultipleConnectionsProperty);

                    if (context.SerializedObject.ApplyModifiedProperties())
                    {
                        EditorUtility.SetDirty(context.Config);
                        context.UpdateWindowTitle();
                    }
                }
            }

            PlayServWindowChrome.EndSectionCard(expanded);
            EditorPrefs.SetBool(Const.PrefFoldConfig, state.FoldConfig);
        }
    }
}
