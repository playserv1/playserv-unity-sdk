#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using Playserv.Deploy.Editor;
using Playserv.Wrapper;

namespace Playserv.Editor
{
    public sealed class PlayServWindow : EditorWindow
    {
        private const string WindowTitlePrefix = "PlayServ";
        private const string FallbackSdkVersion = "0.1.0";
        private const string MenuPath = "Tools/PlayServ/Settings";
        private const string DocsUrl = "https://docs.playserv.io/";
        private const float FixedWindowWidth = 720f;
        private const float MinWindowHeight = 760f;
        private const float MaxWindowHeight = 10000f;
        private const float StyledFieldHeight = 26f;
        private const bool ShowWebSocketConnectionMenu = false;

        private readonly PlayServWindowState _state = new PlayServWindowState();
        private readonly DeploymentClosureFilter _deploymentClosureFilter = new DeploymentClosureFilter();
        private readonly DeploymentZipBuilder _deploymentZipBuilder = new DeploymentZipBuilder();
        private readonly PlayServOverviewPresenter _overviewPresenter = new PlayServOverviewPresenter();
        private readonly PlayServConfigSectionPresenter _configSectionPresenter = new PlayServConfigSectionPresenter();
        private readonly PlayServDeploymentSectionPresenter _deploymentSectionPresenter = new PlayServDeploymentSectionPresenter();
        private readonly PlayServModelSectionPresenter _modelSectionPresenter = new PlayServModelSectionPresenter();
        private readonly PlayServEventsSectionPresenter _eventsSectionPresenter = new PlayServEventsSectionPresenter();
        private readonly PlayServCodegenSectionPresenter _codegenSectionPresenter = new PlayServCodegenSectionPresenter();
        private readonly PlayServConnectionSectionPresenter _connectionSectionPresenter = new PlayServConnectionSectionPresenter();
        private readonly PlayServModuleSettingsPresenter _moduleSettingsPresenter = new PlayServModuleSettingsPresenter();

        private VersionSyncAction _versionSyncAction;
        private DeploymentUploadAction _deploymentUploadAction;
        private PlayServDeploymentController _deploymentController;
        private PlayServConnectionController _connectionController;

        [MenuItem(MenuPath)]
        public static void ShowFromMenu() => ShowWindow();

        [InitializeOnLoadMethod]
        private static void OnEditorLoad()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            if (!EditorPrefs.HasKey(Const.PrefKeyShowOnStartup))
                EditorPrefs.SetBool(Const.PrefKeyShowOnStartup, true);

            if (!EditorPrefs.HasKey(Const.PrefKeyAutoCodegen))
                EditorPrefs.SetBool(Const.PrefKeyAutoCodegen, true);

            if (EditorPrefs.GetBool(Const.PrefKeyShowOnStartup, true))
                EditorApplication.delayCall += ShowWindow;
        }

        private static void ShowWindow()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            var window = GetWindow<PlayServWindow>(title: WindowTitlePrefix);
            window.titleContent = new GUIContent(BuildWindowTitle(PlayServConfigProvider.FindExisting()?.SdkVersion));
            window.ConfigureWindowSize();
            window.Show();
            window.Focus();
        }

        private void OnEnable()
        {
            EnsureControllers();

            _state.FoldCodegen = EditorPrefs.GetBool(Const.PrefFoldCodegen, true);
            _state.FoldEvents = EditorPrefs.GetBool(Const.PrefFoldEvents, true);
            _state.FoldModel = EditorPrefs.GetBool(Const.PrefFoldModel, true);
            _state.FoldConfig = EditorPrefs.GetBool(Const.PrefFoldConfig, true);
            _state.FoldConnection = EditorPrefs.GetBool(Const.PrefFoldConnection, false);
            _state.FoldDeployment = EditorPrefs.GetBool(Const.PrefFoldDeployment, false);
            _state.ShowModuleSettingsLayer = false;
            _state.ModuleSettings.Load();

            _state.ShowAvailableSchemaInfo = false;

            ConfigureWindowSize();

            _state.WebSocketEndpoint = EditorPrefs.GetString(
                Const.PrefKeyWebSocketEndpoint,
                PlayServPackageDefaultsProvider.ResolveBackendServerAddress(null));

            EnsureConfig();
            UpdateWindowTitle();
        }

        private void OnDisable()
        {
            _connectionController?.Dispose();
            _deploymentController?.Dispose();
        }

        private void OnGUI()
        {
            EnforceFixedWidth();
            PlayServWindowTheme.Ensure();
            DrawWindowBackdrop();

            var previousLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = Mathf.Clamp(position.width * 0.23f, 120f, 170f);

            var context = CreateContext();

            using (new GUILayout.AreaScope(new Rect(0f, 0f, position.width, position.height)))
            {
                _state.MainScrollPos = EditorGUILayout.BeginScrollView(_state.MainScrollPos, GUIStyle.none, GUI.skin.verticalScrollbar);

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(24f);
                    using (new EditorGUILayout.VerticalScope())
                    {
                        GUILayout.Space(18f);
                        if (_state.ShowModuleSettingsLayer)
                            DrawModuleSettingsLayer(context);
                        else
                            DrawMainLayer(context);

                        GUILayout.Space(18f);
                    }
                    GUILayout.Space(24f);
                }

                EditorGUILayout.EndScrollView();
            }

            EditorGUIUtility.labelWidth = previousLabelWidth;

            if (_state.DeployRunning)
                Repaint();
        }

        private void DrawMainLayer(PlayServWindowContext context)
        {
            _overviewPresenter.DrawHeader(context);
            GUILayout.Space(18f);

            _configSectionPresenter.Draw(context);

            if (_state.ModuleSettings.Deployment || _state.DeployRunning || _state.VersionSyncRunning)
            {
                GUILayout.Space(12f);
                _deploymentSectionPresenter.Draw(context);
            }

            if (_state.ModuleSettings.ModelSync)
            {
                GUILayout.Space(12f);
                _modelSectionPresenter.Draw(context);
            }

            if (_state.ModuleSettings.Events)
            {
                GUILayout.Space(12f);
                _eventsSectionPresenter.Draw(context);
            }

            if (_state.ModuleSettings.Codegen)
            {
                GUILayout.Space(12f);
                _codegenSectionPresenter.Draw(context);
            }

            if (ShowWebSocketConnectionMenu)
            {
                GUILayout.Space(12f);
                _connectionSectionPresenter.Draw(context);
            }

            GUILayout.Space(16f);
            _overviewPresenter.DrawFooter(context);
        }

        private void DrawModuleSettingsLayer(PlayServWindowContext context)
        {
            _moduleSettingsPresenter.Draw(context);
        }

        private void EnsureControllers()
        {
            if (_versionSyncAction == null)
                _versionSyncAction = new VersionSyncAction(_deploymentZipBuilder);

            if (_deploymentUploadAction == null)
                _deploymentUploadAction = new DeploymentUploadAction(_deploymentZipBuilder);

            if (_deploymentController == null)
                _deploymentController = new PlayServDeploymentController(
                    _state,
                    _deploymentClosureFilter,
                    _deploymentUploadAction,
                    _versionSyncAction,
                    Repaint);

            if (_connectionController == null)
                _connectionController = new PlayServConnectionController(_state, Repaint);
        }

        private void EnsureConfig()
        {
            _state.Config = PlayServConfigProvider.GetOrCreate();
            _state.SerializedObject = new SerializedObject(_state.Config);

            _state.GameAccessTokenProperty = _state.SerializedObject.FindProperty("gameAccessToken");
            _state.GameIdProperty = _state.SerializedObject.FindProperty("gameId");
            _state.GameVersionProperty = _state.SerializedObject.FindProperty("gameVersion");
            _state.SdkVersionProperty = _state.SerializedObject.FindProperty("sdkVersion");
            _state.AllowMultipleConnectionsProperty = _state.SerializedObject.FindProperty("allowMultipleConnections");
            _state.DeployAuthTokenProperty = _state.SerializedObject.FindProperty("deployAuthToken");
            _state.DeployTimeoutSecondsProperty = _state.SerializedObject.FindProperty("timeoutSeconds");

            UpdateWindowTitle();
        }

        private void UpdateWindowTitle()
        {
            titleContent = new GUIContent(BuildWindowTitle(_state.Config != null ? _state.Config.SdkVersion : null));
        }

        private static string BuildWindowTitle(string sdkVersion)
        {
            var version = string.IsNullOrWhiteSpace(sdkVersion) ? FallbackSdkVersion : sdkVersion.Trim();
            return $"{WindowTitlePrefix} {version}";
        }

        private void DrawWindowBackdrop()
        {
            var fullRect = new Rect(0f, 0f, position.width, position.height);
            EditorGUI.DrawRect(fullRect, PlayServWindowTheme.Background);
            EditorGUI.DrawRect(new Rect(0f, 0f, position.width, 1f), PlayServWindowTheme.GridLine);
        }

        private void ConfigureWindowSize()
        {
            minSize = new Vector2(FixedWindowWidth, MinWindowHeight);
            maxSize = new Vector2(FixedWindowWidth, MaxWindowHeight);
            EnforceFixedWidth();
        }

        private void EnforceFixedWidth()
        {
            if (Mathf.Approximately(position.width, FixedWindowWidth))
                return;

            position = new Rect(
                position.x,
                position.y,
                FixedWindowWidth,
                Mathf.Max(position.height, MinWindowHeight));
        }

        private void FocusConfigAsset()
        {
            EnsureConfig();
            if (_state.Config == null)
                return;

            Selection.activeObject = _state.Config;
            EditorGUIUtility.PingObject(_state.Config);
        }

        private PlayServWindowContext CreateContext()
        {
            return new PlayServWindowContext(
                this,
                _state,
                _deploymentController,
                _connectionController,
                DocsUrl,
                StyledFieldHeight,
                EnsureConfig,
                UpdateWindowTitle,
                FocusConfigAsset,
                Repaint);
        }
    }
}
#endif
