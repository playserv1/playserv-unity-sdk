using System;
using UnityEditor;
using UnityEngine;

namespace Playserv.Editor
{
    internal sealed class PlayServDeploymentSectionPresenter : IDisposable
    {
        private readonly Func<PlayServWindowContext, PlayServDeploymentController> _getController;
        private readonly PlatformFunctionPanel _platformFunctions = new PlatformFunctionPanel();
        private readonly ServerImagePanel _serverImages = new ServerImagePanel();
        private int _mode;

        public void Dispose() { _platformFunctions.Dispose(); _serverImages.Dispose(); }

        public PlayServDeploymentSectionPresenter(Func<PlayServWindowContext, PlayServDeploymentController> getController)
        {
            _getController = getController ?? throw new ArgumentNullException(nameof(getController));
        }

        public void Draw(PlayServWindowContext context)
        {
            var state = context.State;
            var controller = _getController(context);
            var expanded = PlayServWindowChrome.BeginSectionCard(
                ref state.FoldDeployment,
                "Server Code",
                "Deployment",
                "Deploy server code or publish a game-server image to your project.");

            if (expanded)
            {
                using (new EditorGUI.DisabledScope(state.DeployRunning || state.VersionSyncRunning || _platformFunctions.Running || _serverImages.Running))
                    _mode = GUILayout.Toolbar(_mode, new[] { "RPC", "Platform Functions", "Server Images" });
                if (_mode == 1 || _mode == 2)
                {
                    if (_mode == 1) _platformFunctions.Draw(context.Repaint);
                    else _serverImages.Draw(context.Repaint);
                    PlayServWindowChrome.EndSectionCard(expanded);
                    EditorPrefs.SetBool(Const.PrefFoldDeployment, state.FoldDeployment);
                    return;
                }
                DrawDeployCredential(state);
                GUILayout.Space(6f);

                var selectedFolder = (DefaultAsset)EditorGUILayout.ObjectField(
                    "Folder",
                    state.DeployFolder,
                    typeof(DefaultAsset),
                    false);
                if (selectedFolder != state.DeployFolder)
                {
                    state.DeployFolder = selectedFolder;
                    SaveDeployFolder(selectedFolder);
                }
                var hasDeployFolder = state.DeployFolder != null &&
                                      AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(state.DeployFolder));

                state.DeployIncludeSubfolders = EditorGUILayout.ToggleLeft("Include subfolders", state.DeployIncludeSubfolders);

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Pattern", GUILayout.Width(EditorGUIUtility.labelWidth));
                    state.DeployPattern = EditorGUILayout.TextField(
                        state.DeployPattern,
                        PlayServWindowTheme.InputStyle,
                        GUILayout.Height(context.StyledFieldHeight));
                }

                state.DeployKeepRelativePaths = EditorGUILayout.ToggleLeft(
                    "Keep relative paths in ZIP (recommended)",
                    state.DeployKeepRelativePaths);

                GUILayout.Space(6f);

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(state.DeployRunning || state.VersionSyncRunning || !hasDeployFolder))
                    {
                        if (PlayServWindowChrome.DrawActionButton("Preview Files", PlayServWindowButtonTone.Secondary, GUILayout.Width(112f), GUILayout.Height(30f)))
                        {
                            state.DeployFilesPreview = controller.BuildDeployFileList(out var err);
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
                            _ = controller.StartVersionSyncAsync(context);

                        GUILayout.Space(6f);

                        if (PlayServWindowChrome.DrawActionButton("Deploy Now", PlayServWindowButtonTone.Primary, GUILayout.Width(128f), GUILayout.Height(30f)))
                            _ = controller.StartDeployAsync(context);
                    }

                    GUILayout.Space(6f);

                    using (new EditorGUI.DisabledScope(!state.DeployRunning))
                    {
                        if (PlayServWindowChrome.DrawActionButton("Cancel", PlayServWindowButtonTone.Danger, GUILayout.Width(112f), GUILayout.Height(30f)))
                            controller.CancelDeploy();
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

                GUILayout.Space(8f);
                DrawStatusVersionRow(context);
            }

            PlayServWindowChrome.EndSectionCard(expanded);
            EditorPrefs.SetBool(Const.PrefFoldDeployment, state.FoldDeployment);
        }

        private static void DrawDeployCredential(PlayServWindowState state)
        {
            if (PlayServDeployCredentialStore.HasEnvironmentToken)
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.TextField("Deploy Token", PlayServDeployCredentialStore.EnvironmentVariableName);
                return;
            }

            if (!state.DeployTokenInitialized)
            {
                state.DeployTokenDraft = PlayServDeployCredentialStore.GetLocalToken();
                state.DeployTokenInitialized = true;
            }

            state.DeployTokenDraft = EditorGUILayout.PasswordField(
                "Deploy Token",
                state.DeployTokenDraft ?? string.Empty);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EditorGUIUtility.labelWidth);
                if (PlayServWindowChrome.DrawActionButton(
                        "Save",
                        PlayServWindowButtonTone.Secondary,
                        GUILayout.Width(72f),
                        GUILayout.Height(26f)))
                {
                    PlayServDeployCredentialStore.SetLocalToken(state.DeployTokenDraft);
                    state.DeployTokenDraft = PlayServDeployCredentialStore.GetLocalToken();
                }

                GUILayout.Space(6f);
                using (new EditorGUI.DisabledScope(
                           string.IsNullOrWhiteSpace(state.DeployTokenDraft) &&
                           string.IsNullOrWhiteSpace(PlayServDeployCredentialStore.GetLocalToken())))
                {
                    if (PlayServWindowChrome.DrawActionButton(
                            "Clear",
                            PlayServWindowButtonTone.Ghost,
                            GUILayout.Width(72f),
                            GUILayout.Height(26f)))
                    {
                        PlayServDeployCredentialStore.ClearLocalToken();
                        state.DeployTokenDraft = string.Empty;
                    }
                }
            }
        }

        private static void DrawStatusVersionRow(PlayServWindowContext context)
        {
            var state = context.State;
            var deployStatus = ResolveDeployStatus(state);
            var versionStatus = ResolveVersionStatus(context, state);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Status", PlayServWindowTheme.MiniHeadingStyle, GUILayout.Width(46f));
                GUILayout.Label(deployStatus, ResolveDeployStatusStyle(state), GUILayout.MinWidth(150f));
                GUILayout.Space(8f);
                EditorGUILayout.LabelField("Version", PlayServWindowTheme.MiniHeadingStyle, GUILayout.Width(48f));
                GUILayout.Label(versionStatus, ResolveVersionStatusStyle(state), GUILayout.MinWidth(150f));
            }
        }

        private static string ResolveDeployStatus(PlayServWindowState state)
        {
            if (state.DeployRunning)
                return string.IsNullOrWhiteSpace(state.DeployStatus) ? "Working..." : state.DeployStatus;

            return string.IsNullOrWhiteSpace(state.DeployStatus) ? "Idle" : state.DeployStatus;
        }

        private static GUIStyle ResolveDeployStatusStyle(PlayServWindowState state)
        {
            return !state.DeployRunning &&
                   !string.IsNullOrWhiteSpace(state.DeployStatus) &&
                   state.DeployStatus.StartsWith("Failed", StringComparison.OrdinalIgnoreCase)
                ? PlayServWindowTheme.NoticeWarningStyle
                : PlayServWindowTheme.NoticeInfoStyle;
        }

        private static string ResolveVersionStatus(PlayServWindowContext context, PlayServWindowState state)
        {
            if (state.VersionSyncRunning)
                return string.IsNullOrWhiteSpace(state.VersionSyncStatus) ? "Syncing..." : state.VersionSyncStatus;

            var version = context.GameVersionProperty == null ? null : context.GameVersionProperty.stringValue;
            if (!string.IsNullOrWhiteSpace(version))
                return version.Trim();

            return string.IsNullOrWhiteSpace(state.VersionSyncStatus) ? "Not synced" : state.VersionSyncStatus;
        }

        private static GUIStyle ResolveVersionStatusStyle(PlayServWindowState state)
        {
            return !state.VersionSyncRunning &&
                   !string.IsNullOrWhiteSpace(state.VersionSyncStatus) &&
                   (state.VersionSyncStatus.StartsWith("Sync failed", StringComparison.OrdinalIgnoreCase) ||
                    state.VersionSyncStatus.StartsWith("Hash mismatch", StringComparison.OrdinalIgnoreCase))
                ? PlayServWindowTheme.NoticeWarningStyle
                : PlayServWindowTheme.NoticeInfoStyle;
        }

        private static void SaveDeployFolder(DefaultAsset folder)
        {
            if (folder == null)
            {
                EditorPrefs.DeleteKey(Const.PrefKeyDeploymentFolderAssetPath);
                return;
            }

            var assetPath = AssetDatabase.GetAssetPath(folder);
            if (string.IsNullOrWhiteSpace(assetPath))
                EditorPrefs.DeleteKey(Const.PrefKeyDeploymentFolderAssetPath);
            else
                EditorPrefs.SetString(Const.PrefKeyDeploymentFolderAssetPath, assetPath);
        }
    }
}
