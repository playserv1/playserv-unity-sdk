using System;
using UnityEditor;
using UnityEngine;
using Playserv.Wrapper;

namespace Playserv.Editor
{
    public sealed class PlayServWindow : EditorWindow
    {
        private const string WindowTitlePrefix = "PlayServ";
        private const string MenuPath = "Tools/PlayServ/Settings";
        private const string DocsUrl = "https://docs.playserv.io/";
        private const float FixedWindowWidth = 720f;
        private const float MinWindowHeight = 760f;
        private const float MaxWindowHeight = 10000f;
        private const float HorizontalWindowPadding = 24f;
        private const float VerticalScrollbarReserve = 18f;
        private const float StyledFieldHeight = 26f;
        private static readonly bool ShowWebSocketConnectionMenu = false;

        private readonly PlayServWindowState _state = new PlayServWindowState();
        private readonly PlayServOverviewPresenter _overviewPresenter = new PlayServOverviewPresenter();
        private readonly PlayServConfigSectionPresenter _configSectionPresenter = new PlayServConfigSectionPresenter();
        private readonly PlayServOptionalEditorSection _deploymentSection =
            new PlayServOptionalEditorSection(PlayServEditorSectionIds.Deployment);
        private readonly PlayServOptionalEditorSection _modelSection =
            new PlayServOptionalEditorSection(PlayServEditorSectionIds.ModelSync);
        private readonly PlayServOptionalEditorSection _eventsSection =
            new PlayServOptionalEditorSection(PlayServEditorSectionIds.Events);
        private readonly PlayServOptionalEditorSection _codegenSection =
            new PlayServOptionalEditorSection(PlayServEditorSectionIds.Codegen);
        private readonly PlayServOptionalEditorSection _appleSignInSection =
            new PlayServOptionalEditorSection(PlayServEditorSectionIds.AppleSignIn);
        private readonly PlayServConnectionSectionPresenter _connectionSectionPresenter = new PlayServConnectionSectionPresenter();
        private readonly PlayServModuleSettingsPresenter _moduleSettingsPresenter = new PlayServModuleSettingsPresenter();

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
            window.titleContent = new GUIContent(WindowTitlePrefix);
            window.ConfigureWindowSize();
            window.Show();
            window.Focus();
        }

        private void OnEnable()
        {
            PlayServModuleGraphSynchronizer.QueueSync();
            EnsureControllers();

            _state.FoldCodegen = EditorPrefs.GetBool(Const.PrefFoldCodegen, true);
            _state.FoldEvents = EditorPrefs.GetBool(Const.PrefFoldEvents, true);
            _state.FoldModel = EditorPrefs.GetBool(Const.PrefFoldModel, true);
            _state.FoldConfig = EditorPrefs.GetBool(Const.PrefFoldConfig, true);
            _state.FoldConnection = EditorPrefs.GetBool(Const.PrefFoldConnection, false);
            _state.FoldDeployment = EditorPrefs.GetBool(Const.PrefFoldDeployment, false);
            _state.FoldAppleSignIn = EditorPrefs.GetBool(Const.PrefFoldAppleSignIn, false);
            _state.ShowModuleSettingsLayer = false;
            _state.ModuleSettings.Load();

            _state.ShowAvailableSchemaInfo = false;

            ConfigureWindowSize();

            _state.WebSocketEndpoint = EditorPrefs.GetString(
                Const.PrefKeyWebSocketEndpoint,
                PlayServPackageDefaultsProvider.ResolveBackendServerAddress(null));
            _state.DeployFolder = LoadSavedDeployFolder();

            EnsureConfig();
            UpdateWindowTitle();
        }

        private void OnDisable()
        {
            _connectionController?.Dispose();
            _deploymentSection.Dispose();
            _modelSection.Dispose();
            _eventsSection.Dispose();
            _codegenSection.Dispose();
            _appleSignInSection.Dispose();
        }

        private void OnGUI()
        {
            PlayServWindowTheme.Ensure();
            DrawWindowBackdrop();

            var previousLabelWidth = EditorGUIUtility.labelWidth;
            try
            {
                EditorGUIUtility.labelWidth = Mathf.Clamp(position.width * 0.23f, 120f, 170f);

                var context = CreateContext();

                using (var scrollView = new EditorGUILayout.ScrollViewScope(_state.MainScrollPos, GUIStyle.none, GUI.skin.verticalScrollbar))
                {
                    var contentWidth = Mathf.Max(0f, position.width - (HorizontalWindowPadding * 2f) - VerticalScrollbarReserve);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Space(HorizontalWindowPadding);
                        using (new EditorGUILayout.VerticalScope(GUILayout.Width(contentWidth)))
                        {
                            GUILayout.Space(18f);
                            if (_state.ShowModuleSettingsLayer)
                                DrawModuleSettingsLayer(context);
                            else
                                DrawMainLayer(context);

                            GUILayout.Space(18f);
                        }
                        GUILayout.Space(HorizontalWindowPadding);
                    }

                    _state.MainScrollPos = scrollView.scrollPosition;
                }
            }
            finally
            {
                EditorGUIUtility.labelWidth = previousLabelWidth;
            }

            if (_state.DeployRunning)
                Repaint();
        }

        private void DrawMainLayer(PlayServWindowContext context)
        {
            _overviewPresenter.DrawHeader(context);
            GUILayout.Space(18f);

            _configSectionPresenter.Draw(context);

            if (PlayServEditorModuleAvailability.EditorDeployment &&
                (_state.ModuleSettings.Deployment || _state.DeployRunning || _state.VersionSyncRunning) &&
                _deploymentSection.IsAvailable)
            {
                GUILayout.Space(12f);
                _deploymentSection.Draw(context);
            }

            if (PlayServEditorModuleAvailability.EditorModelSync &&
                _state.ModuleSettings.ModelSync &&
                _modelSection.IsAvailable)
            {
                GUILayout.Space(12f);
                _modelSection.Draw(context);
            }

            if (PlayServEditorModuleAvailability.EditorEvents &&
                _state.ModuleSettings.RuntimeEvents &&
                _eventsSection.IsAvailable)
            {
                GUILayout.Space(12f);
                _eventsSection.Draw(context);
            }

            if (PlayServEditorModuleAvailability.EditorCodegen &&
                _state.ModuleSettings.Codegen &&
                _codegenSection.IsAvailable)
            {
                GUILayout.Space(12f);
                _codegenSection.Draw(context);
            }

            if (PlayServEditorModuleAvailability.RuntimeAppleSignIn &&
                _state.ModuleSettings.RuntimeAppleSignIn &&
                _appleSignInSection.IsAvailable)
            {
                GUILayout.Space(12f);
                _appleSignInSection.Draw(context);
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
            if (_connectionController == null)
                _connectionController = new PlayServConnectionController(_state, Repaint);
        }

        private void EnsureConfig()
        {
            _state.Config = PlayServConfigProvider.GetOrCreate();
            _state.SerializedObject = new SerializedObject(_state.Config);

            _state.ClientTokenProperty = _state.SerializedObject.FindProperty("clientToken");
            _state.AuthorizationProperty = _state.SerializedObject.FindProperty("authorization");
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
            titleContent = new GUIContent(WindowTitlePrefix);
        }

        private static DefaultAsset LoadSavedDeployFolder()
        {
            var assetPath = EditorPrefs.GetString(Const.PrefKeyDeploymentFolderAssetPath, string.Empty);
            if (string.IsNullOrWhiteSpace(assetPath) || !AssetDatabase.IsValidFolder(assetPath))
                return null;

            return AssetDatabase.LoadAssetAtPath<DefaultAsset>(assetPath);
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
