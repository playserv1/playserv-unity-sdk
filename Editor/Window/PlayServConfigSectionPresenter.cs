using System;
using UnityEditor;
using UnityEngine;
using Playserv.Wrapper;

namespace Playserv.Editor
{
    internal sealed class PlayServConfigSectionPresenter
    {
        private readonly PlayServServerTokenField _serverToken = new PlayServServerTokenField();

        public void Draw(PlayServWindowContext context)
        {
            var state = context.State;
            var expanded = PlayServWindowChrome.BeginSectionCard(
                ref state.FoldConfig,
                "Control Room",
                "PlayServ Config",
                "Manage runtime identity, configurable endpoints, SDK version, and the project-side config asset from one place.");

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

                    PlayServDeploymentSettings.MigrateDashboard(context.Config);
                    context.SerializedObject.Update();

                    if (PlayServEnvironmentClientTokens.IsManaged(context.Config))
                        PlayServEnvironmentClientTokens.DrawTokenField(context.Config);
                    else
                        EditorGUILayout.PropertyField(context.ClientTokenProperty);
                    _serverToken.Draw(context.Config);
                    EditorGUILayout.PropertyField(context.DeploymentGameIdProperty);
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
