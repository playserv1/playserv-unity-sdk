#if UNITY_EDITOR && !PLAYSERV_DISABLE_EDITOR_DEPLOYMENT
using System;
using UnityEditor;
using UnityEngine;

namespace Playserv.Editor
{
    internal sealed class PlayServDeploymentSectionPresenter
    {
        public void Draw(PlayServWindowContext context)
        {
            var state = context.State;
            var expanded = PlayServWindowChrome.BeginSectionCard(
                ref state.FoldDeployment,
                "Release",
                "Deployment",
                "Preview RPC code closure, sync deployed version, and ship the ZIP package to the active deployment endpoint.");

            if (expanded)
            {
                PlayServWindowChrome.DrawNotice(
                    "Create a ZIP from selected files and upload it to your Deployment API endpoint.",
                    MessageType.Info);

                if (context.SerializedObject != null)
                {
                    context.SerializedObject.Update();

                    if (context.DeployTimeoutSecondsProperty != null)
                        EditorGUILayout.PropertyField(context.DeployTimeoutSecondsProperty, new GUIContent("Timeout Seconds"));

                    if (context.DeployAuthTokenProperty != null)
                    {
                        var updatedToken = EditorGUILayout.PasswordField("Deploy Auth Token", context.DeployAuthTokenProperty.stringValue);
                        if (!string.Equals(updatedToken, context.DeployAuthTokenProperty.stringValue, StringComparison.Ordinal))
                            context.DeployAuthTokenProperty.stringValue = updatedToken;
                    }

                    if (context.SerializedObject.ApplyModifiedProperties())
                        EditorUtility.SetDirty(context.Config);
                }

                GUILayout.Space(4f);
                state.DeployFolder = (DefaultAsset)EditorGUILayout.ObjectField(
                    "Folder",
                    state.DeployFolder,
                    typeof(DefaultAsset),
                    false);

                state.DeployIncludeSubfolders = EditorGUILayout.ToggleLeft("Include subfolders", state.DeployIncludeSubfolders);

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Pattern", GUILayout.Width(EditorGUIUtility.labelWidth));
                    state.DeployPattern = EditorGUILayout.TextField(state.DeployPattern, PlayServWindowTheme.InputStyle);
                }

                state.DeployKeepRelativePaths = EditorGUILayout.ToggleLeft(
                    "Keep relative paths in ZIP (recommended)",
                    state.DeployKeepRelativePaths);

                GUILayout.Space(6f);

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(state.DeployRunning || state.VersionSyncRunning))
                    {
                        if (PlayServWindowChrome.DrawActionButton("Preview Files", PlayServWindowButtonTone.Secondary, GUILayout.Width(112f), GUILayout.Height(30f)))
                        {
                            state.DeployFilesPreview = context.DeploymentController.BuildDeployFileList(out var err);
                            if (!string.IsNullOrEmpty(err))
                            {
                                state.DeployStatus = err;
                                state.DeployShowFileList = false;
                                state.DeployFilesPreview.Clear();
                            }
                            else
                            {
                                state.DeployStatus = "Preview ready.";
                                state.DeployShowFileList = true;
                            }
                        }

                        GUILayout.Space(6f);

                        if (PlayServWindowChrome.DrawActionButton("Clear Preview", PlayServWindowButtonTone.Ghost, GUILayout.Width(112f), GUILayout.Height(30f)))
                        {
                            state.DeployFilesPreview.Clear();
                            state.DeployShowFileList = false;
                        }

                        GUILayout.Space(6f);

                        if (PlayServWindowChrome.DrawActionButton("Sync Version", PlayServWindowButtonTone.Secondary, GUILayout.Width(112f), GUILayout.Height(30f)))
                            _ = context.DeploymentController.StartVersionSyncAsync(context);

                        GUILayout.Space(6f);

                        if (PlayServWindowChrome.DrawActionButton("Deploy Now", PlayServWindowButtonTone.Primary, GUILayout.Width(128f), GUILayout.Height(30f)))
                            _ = context.DeploymentController.StartDeployAsync(context);
                    }

                    GUILayout.Space(6f);

                    using (new EditorGUI.DisabledScope(!state.DeployRunning))
                    {
                        if (PlayServWindowChrome.DrawActionButton("Cancel", PlayServWindowButtonTone.Danger, GUILayout.Width(112f), GUILayout.Height(30f)))
                            context.DeploymentController.CancelDeploy();
                    }
                }

                if (state.DeployShowFileList && state.DeployFilesPreview.Count > 0)
                {
                    GUILayout.Space(6f);
                    EditorGUILayout.LabelField($"Files ({state.DeployFilesPreview.Count})", PlayServWindowTheme.MiniHeadingStyle);

                    using (new EditorGUILayout.VerticalScope(PlayServWindowTheme.LogContainerStyle))
                    {
                        state.DeployFilesScroll = EditorGUILayout.BeginScrollView(state.DeployFilesScroll, GUILayout.Height(140f));
                        for (var i = 0; i < state.DeployFilesPreview.Count; i++)
                            EditorGUILayout.LabelField(state.DeployFilesPreview[i], PlayServWindowTheme.LogLineStyle);
                        EditorGUILayout.EndScrollView();
                    }
                }

                if (state.DeployRunning || !string.IsNullOrWhiteSpace(state.DeployStatus))
                {
                    GUILayout.Space(6f);
                    EditorGUILayout.LabelField("Status", PlayServWindowTheme.MiniHeadingStyle);
                    var deployMessageType = !state.DeployRunning && state.DeployStatus.StartsWith("Failed", StringComparison.OrdinalIgnoreCase)
                        ? MessageType.Warning
                        : MessageType.Info;
                    PlayServWindowChrome.DrawNotice(string.IsNullOrEmpty(state.DeployStatus) ? "Working..." : state.DeployStatus, deployMessageType);

                    if (state.DeployRunning)
                        EditorGUILayout.Slider("Progress", state.DeployProgress, 0f, 1f);
                }

                if (state.VersionSyncRunning || !string.IsNullOrWhiteSpace(state.VersionSyncStatus))
                {
                    GUILayout.Space(6f);
                    EditorGUILayout.LabelField("Version Sync", PlayServWindowTheme.MiniHeadingStyle);
                    PlayServWindowChrome.DrawNotice(state.VersionSyncStatus, state.VersionSyncRunning ? MessageType.Info : MessageType.Warning);
                }
            }

            PlayServWindowChrome.EndSectionCard(expanded);
            EditorPrefs.SetBool(Const.PrefFoldDeployment, state.FoldDeployment);
        }
    }
}
#endif
