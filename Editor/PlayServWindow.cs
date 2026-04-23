#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Playserv.CodeGenerator.Editor;
using Playserv.Deploy.Editor;
using Playserv.Editor.Proxy;
using Playserv.Events.Editor;
using Playserv.ModelGenerator.Editor;
using Playserv.Wrapper;

namespace Playserv.Editor
{
    public sealed class PlayServWindow : EditorWindow
    {
        private const string WindowTitlePrefix = "PlayServ";
        private const string FallbackSdkVersion = "0.1.0";
        private const string MenuPath = "Tools/PlayServ/Settings";
        private const string DocsUrl = "https://docs.playserv.io/";
        private const float DefaultWindowWidth = 720f;
        private const float MinWindowWidth = 640f;
        private const float StyledFieldHeight = 26f;
        private const bool ShowWebSocketConnectionMenu = false;

        private readonly DeploymentClosureFilter _deploymentClosureFilter = new DeploymentClosureFilter();
        private readonly DeploymentZipBuilder _deploymentZipBuilder = new DeploymentZipBuilder();

        private PlayServConfig _config;
        private SerializedObject _so;

        private SerializedProperty _pGameAccessToken;
        private SerializedProperty _pGameId;
        private SerializedProperty _pGameVersion;
        private SerializedProperty _pSdkVersion;
        private SerializedProperty _pAllowMultipleConnections;
        private SerializedProperty _pDeployAuthToken;
        private SerializedProperty _pDeployTimeoutSeconds;

        private bool _foldCodegen;
        private bool _foldEvents;
        private bool _foldModel;
        private bool _foldConfig;
        private bool _foldConnection;
        private bool _foldDeployment;

        private bool _showAvailableSchemaInfo;
        private Vector2 _mainScrollPos;

        private EditorWebSocketTransport _wsTransport;
        private Vector2 _connectionScrollPos;
        private string _wsEndpoint = string.Empty;
        private string _testMessage = "{\"type\":\"ping\"}";

        private DefaultAsset _deployFolder;
        private bool _deployIncludeSubfolders = true;
        private string _deployPattern = "*";
        private bool _deployKeepRelativePaths = true;
        private bool _deployShowFileList;
        private Vector2 _deployFilesScroll;
        private List<string> _deployFilesPreview = new List<string>();

        private bool _deployRunning;
        private float _deployProgress;
        private string _deployStatus = string.Empty;
        private CancellationTokenSource _deployCts;
        private bool _versionSyncRunning;
        private string _versionSyncStatus = string.Empty;

        private VersionSyncAction _versionSyncAction;
        private DeploymentUploadAction _deploymentUploadAction;

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
            window.minSize = new Vector2(MinWindowWidth, 760f);
            if (window.position.width > DefaultWindowWidth)
                window.position = new Rect(window.position.x, window.position.y, DefaultWindowWidth, Mathf.Max(window.position.height, 760f));
            window.Show();
            window.Focus();
        }

        private void OnEnable()
        {
            EnsureDeploymentServices();

            _foldCodegen = EditorPrefs.GetBool(Const.PrefFoldCodegen, true);
            _foldEvents = EditorPrefs.GetBool(Const.PrefFoldEvents, true);
            _foldModel = EditorPrefs.GetBool(Const.PrefFoldModel, true);
            _foldConfig = EditorPrefs.GetBool(Const.PrefFoldConfig, true);
            _foldConnection = EditorPrefs.GetBool(Const.PrefFoldConnection, false);
            _foldDeployment = EditorPrefs.GetBool(Const.PrefFoldDeployment, false);

            _showAvailableSchemaInfo = false;

            if (position.width > DefaultWindowWidth)
                position = new Rect(position.x, position.y, DefaultWindowWidth, Mathf.Max(position.height, 760f));

            _wsEndpoint = EditorPrefs.GetString(
                Const.PrefKeyWebSocketEndpoint,
                PlayServPackageDefaultsProvider.ResolveBackendServerAddress(null));

            EnsureConfig();
            UpdateWindowTitle();
        }

        private void OnDisable()
        {
            _wsTransport?.Dispose();
            _wsTransport = null;

            _deployCts?.Cancel();
            _deployCts?.Dispose();
            _deployCts = null;

            if (_deployRunning)
            {
                _deployRunning = false;
                EditorUtility.ClearProgressBar();
            }
        }

        private void OnGUI()
        {
            PlayServWindowTheme.Ensure();
            DrawWindowBackdrop();

            var previousLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = Mathf.Clamp(position.width * 0.23f, 120f, 170f);

            using (new GUILayout.AreaScope(new Rect(0f, 0f, position.width, position.height)))
            {
                _mainScrollPos = EditorGUILayout.BeginScrollView(_mainScrollPos, GUIStyle.none, GUI.skin.verticalScrollbar);

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(24f);
                    using (new EditorGUILayout.VerticalScope())
                    {
                        GUILayout.Space(18f);
                        DrawHeroSection();
                        GUILayout.Space(14f);
                        DrawOverviewStrip();
                        GUILayout.Space(18f);

                        DrawConfigFoldout();
                        GUILayout.Space(12f);
                        DrawDeploymentFoldout();
                        GUILayout.Space(12f);
                        DrawModelFoldout();
                        GUILayout.Space(12f);
                        DrawEventsFoldout();
                        GUILayout.Space(12f);
                        DrawCodegenFoldout();

                        if (ShowWebSocketConnectionMenu)
                        {
                            GUILayout.Space(12f);
                            DrawConnectionFoldout();
                        }

                        GUILayout.Space(16f);
                        DrawFooter();
                        GUILayout.Space(18f);
                    }
                    GUILayout.Space(24f);
                }

                EditorGUILayout.EndScrollView();
            }

            EditorGUIUtility.labelWidth = previousLabelWidth;

            if (_deployRunning)
                Repaint();
        }

        private void EnsureDeploymentServices()
        {
            if (_versionSyncAction == null)
                _versionSyncAction = new VersionSyncAction(_deploymentZipBuilder);

            if (_deploymentUploadAction == null)
                _deploymentUploadAction = new DeploymentUploadAction(_deploymentZipBuilder);
        }

        private void EnsureConfig()
        {
            _config = PlayServConfigProvider.GetOrCreate();
            _so = new SerializedObject(_config);

            _pGameAccessToken = _so.FindProperty("gameAccessToken");
            _pGameId = _so.FindProperty("gameId");
            _pGameVersion = _so.FindProperty("gameVersion");
            _pSdkVersion = _so.FindProperty("sdkVersion");
            _pAllowMultipleConnections = _so.FindProperty("allowMultipleConnections");
            _pDeployAuthToken = _so.FindProperty("deployAuthToken");
            _pDeployTimeoutSeconds = _so.FindProperty("timeoutSeconds");

            UpdateWindowTitle();
        }

        private void UpdateWindowTitle()
        {
            titleContent = new GUIContent(BuildWindowTitle(_config != null ? _config.SdkVersion : null));
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

        private void DrawHeroSection()
        {
            using (new EditorGUILayout.VerticalScope(PlayServWindowTheme.HeroCardStyle))
            {
                GUILayout.Label("PlayServ editor controls", PlayServWindowTheme.HeroTitleStyle);
                GUILayout.Space(6f);
                GUILayout.Label("Configure runtime, sync models, deploy code, and generate APIs from one place.", PlayServWindowTheme.HeroAccentStyle);
            }
        }

        private void DrawOverviewStrip()
        {
            using (new EditorGUILayout.VerticalScope())
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawOverviewCard("Game ID", string.IsNullOrWhiteSpace(_config?.GameId) ? "Not configured" : _config.GameId, "Runtime identity");
                    GUILayout.Space(10f);
                    DrawOverviewCard(
                        "Backend",
                        string.IsNullOrWhiteSpace(_config?.BackendServerAddress) ? "Not set" : _config.BackendServerAddress,
                        "Primary transport");
                }

                GUILayout.Space(10f);

                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawOverviewCard("Schema", EditorPrefs.GetString(Const.PrefKeyJsonSchemaVersion, "—"), "Current model hash");
                    GUILayout.Space(10f);
                    DrawOverviewCard("Deploy", _deployRunning ? "Deploying…" : _versionSyncRunning ? "Syncing…" : "Ready", "Release control");
                }
            }
        }

        private void DrawOverviewCard(string label, string value, string caption)
        {
            using (new EditorGUILayout.VerticalScope(PlayServWindowTheme.MetricCardStyle, GUILayout.MinHeight(82f)))
            {
                GUILayout.Label(label, PlayServWindowTheme.MetricLabelStyle);
                GUILayout.Space(2f);
                GUILayout.Label(value, PlayServWindowTheme.MetricValueStyle);
                GUILayout.Space(6f);
                GUILayout.Label(caption, PlayServWindowTheme.MetricCaptionStyle);
            }
        }

        private bool BeginSectionCard(ref bool expanded, string badge, string title, string subtitle)
        {
            EditorGUILayout.BeginVertical(PlayServWindowTheme.CardStyle);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(badge, PlayServWindowTheme.SectionPillStyle, GUILayout.Height(22f));
                GUILayout.FlexibleSpace();

                if (DrawActionButton(expanded ? "Collapse" : "Expand", PlayServWindowButtonTone.Ghost, GUILayout.Width(92f), GUILayout.Height(28f)))
                    expanded = !expanded;
            }

            GUILayout.Space(6f);

            if (GUILayout.Button(title, PlayServWindowTheme.SectionTitleButtonStyle, GUILayout.Height(28f)))
                expanded = !expanded;

            GUILayout.Space(2f);
            GUILayout.Label(subtitle, PlayServWindowTheme.SectionSubtitleStyle);

            if (expanded)
            {
                GUILayout.Space(12f);
                EditorGUILayout.BeginVertical(PlayServWindowTheme.CardBodyStyle);
                return true;
            }

            return false;
        }

        private static void EndSectionCard(bool expanded)
        {
            if (expanded)
                EditorGUILayout.EndVertical();

            EditorGUILayout.EndVertical();
        }

        private bool DrawActionButton(string label, PlayServWindowButtonTone tone, params GUILayoutOption[] options)
        {
            return GUILayout.Button(label, PlayServWindowTheme.GetButtonStyle(tone), options);
        }

        private static void DrawNotice(string text, MessageType type)
        {
            var style = type == MessageType.Warning
                ? PlayServWindowTheme.NoticeWarningStyle
                : PlayServWindowTheme.NoticeInfoStyle;

            GUILayout.Label(text, style);
        }

        private void FocusConfigAsset()
        {
            EnsureConfig();
            if (_config == null)
                return;

            Selection.activeObject = _config;
            EditorGUIUtility.PingObject(_config);
        }

        private static void DrawReadOnlyTextField(string label, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(label);
                EditorGUILayout.SelectableLabel(
                    value ?? string.Empty,
                    PlayServWindowTheme.InputStyle,
                    GUILayout.Height(StyledFieldHeight));
            }
        }

        private void DrawFooter()
        {
            using (new EditorGUILayout.VerticalScope(PlayServWindowTheme.FooterCardStyle))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool showOnStartup = EditorPrefs.GetBool(Const.PrefKeyShowOnStartup, true);
                    bool newShowOnStartup = EditorGUILayout.ToggleLeft("Show this window on Unity startup", showOnStartup);

                    if (newShowOnStartup != showOnStartup)
                        EditorPrefs.SetBool(Const.PrefKeyShowOnStartup, newShowOnStartup);

                    GUILayout.FlexibleSpace();

                    if (DrawActionButton("Ping Config", PlayServWindowButtonTone.Secondary, GUILayout.Width(116f), GUILayout.Height(30f)))
                        FocusConfigAsset();

                    GUILayout.Space(8f);

                    if (DrawActionButton("Open Docs", PlayServWindowButtonTone.Secondary, GUILayout.Width(112f), GUILayout.Height(30f)))
                        Application.OpenURL(DocsUrl);
                }
            }
        }

        private void DrawConfigFoldout()
        {
            var expanded = BeginSectionCard(
                ref _foldConfig,
                "Control Room",
                "PlayServ Config",
                "Manage runtime identity, fixed endpoints, SDK version, and the project-side config asset from one place.");

            if (expanded)
            {
                if (_config == null || _so == null)
                {
                    if (DrawActionButton("Create / Locate Config", PlayServWindowButtonTone.Primary, GUILayout.Width(176f), GUILayout.Height(32f)))
                        EnsureConfig();

                    DrawNotice("Config asset not found.", MessageType.Warning);
                }
                else
                {
                    bool configUiChanged;
                    if (PlayServClientProjectConfigBridge.TryDraw(out configUiChanged))
                    {
                        GUILayout.Space(6);

                        if (configUiChanged)
                        {
                            EnsureConfig();
                            Repaint();
                        }
                    }

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.ObjectField("Config Asset", _config, typeof(PlayServConfig), false);
                    if (DrawActionButton("Ping", PlayServWindowButtonTone.Secondary, GUILayout.Width(72f), GUILayout.Height(30f)))
                        EditorGUIUtility.PingObject(_config);
                    EditorGUILayout.EndHorizontal();

                    GUILayout.Space(6);

                    _so.Update();

                    EditorGUILayout.PropertyField(_pGameAccessToken);
                    EditorGUILayout.PropertyField(_pGameId);
                    EditorGUILayout.PropertyField(_pGameVersion);
                    EditorGUILayout.PropertyField(_pAllowMultipleConnections);
                    DrawReadOnlyTextField("Backend Server Address", _config.BackendServerAddress);
                    DrawReadOnlyTextField("Deploy API Server", _config.DeployApiServerAddress);
                    DrawReadOnlyTextField("Schema API Server", _config.SchemaApiServerAddress);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.PrefixLabel("SDK Version");
                        EditorGUILayout.SelectableLabel(
                            _pSdkVersion.stringValue,
                            PlayServWindowTheme.InputStyle,
                            GUILayout.Height(StyledFieldHeight));
                    }

                    if (_so.ApplyModifiedProperties())
                        EditorUtility.SetDirty(_config);
                }
            }

            EndSectionCard(expanded);
            EditorPrefs.SetBool(Const.PrefFoldConfig, _foldConfig);
        }

        private void DrawDeploymentFoldout()
        {
            var expanded = BeginSectionCard(
                ref _foldDeployment,
                "Release",
                "Deployment",
                "Preview RPC code closure, sync deployed version, and ship the ZIP package to the active deployment endpoint.");

            if (expanded)
            {
                DrawNotice(
                    "Create a ZIP from selected files and upload it to your Deployment API endpoint.",
                    MessageType.Info);

                if (_so != null)
                {
                    _so.Update();

                    if (_pDeployTimeoutSeconds != null)
                        EditorGUILayout.PropertyField(_pDeployTimeoutSeconds, new GUIContent("Timeout Seconds"));

                    if (_pDeployAuthToken != null)
                    {
                        var updatedToken = EditorGUILayout.PasswordField("Deploy Auth Token", _pDeployAuthToken.stringValue);
                        if (!string.Equals(updatedToken, _pDeployAuthToken.stringValue, StringComparison.Ordinal))
                            _pDeployAuthToken.stringValue = updatedToken;
                    }

                    if (_so.ApplyModifiedProperties())
                        EditorUtility.SetDirty(_config);
                }

                GUILayout.Space(4);
                _deployFolder = (DefaultAsset)EditorGUILayout.ObjectField(
                    "Folder",
                    _deployFolder,
                    typeof(DefaultAsset),
                    false);

                _deployIncludeSubfolders = EditorGUILayout.ToggleLeft("Include subfolders", _deployIncludeSubfolders);

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Pattern", GUILayout.Width(EditorGUIUtility.labelWidth));
                    _deployPattern = EditorGUILayout.TextField(_deployPattern, PlayServWindowTheme.InputStyle);
                }

                _deployKeepRelativePaths = EditorGUILayout.ToggleLeft(
                    "Keep relative paths in ZIP (recommended)",
                    _deployKeepRelativePaths);

                GUILayout.Space(6);

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(_deployRunning || _versionSyncRunning))
                    {
                        if (DrawActionButton("Preview Files", PlayServWindowButtonTone.Secondary, GUILayout.Width(120f), GUILayout.Height(30f)))
                        {
                            _deployFilesPreview = BuildDeployFileList(out var err);
                            if (!string.IsNullOrEmpty(err))
                            {
                                _deployStatus = err;
                                _deployShowFileList = false;
                                _deployFilesPreview.Clear();
                            }
                            else
                            {
                                _deployStatus = "Preview ready.";
                                _deployShowFileList = true;
                            }
                        }

                        GUILayout.Space(8f);

                        if (DrawActionButton("Clear Preview", PlayServWindowButtonTone.Ghost, GUILayout.Width(120f), GUILayout.Height(30f)))
                        {
                            _deployFilesPreview.Clear();
                            _deployShowFileList = false;
                        }

                        GUILayout.Space(8f);

                        if (DrawActionButton("Sync Version", PlayServWindowButtonTone.Secondary, GUILayout.Width(120f), GUILayout.Height(30f)))
                            _ = StartVersionSyncAsync();

                        GUILayout.Space(8f);

                        if (DrawActionButton("Deploy Now", PlayServWindowButtonTone.Primary, GUILayout.Width(140f), GUILayout.Height(30f)))
                            _ = StartDeployAsync();
                    }

                    GUILayout.Space(8f);

                    using (new EditorGUI.DisabledScope(!_deployRunning))
                    {
                        if (DrawActionButton("Cancel", PlayServWindowButtonTone.Danger, GUILayout.Width(120f), GUILayout.Height(30f)))
                            _deployCts?.Cancel();
                    }
                }

                if (_deployShowFileList && _deployFilesPreview.Count > 0)
                {
                    GUILayout.Space(6);
                    EditorGUILayout.LabelField($"Files ({_deployFilesPreview.Count})", PlayServWindowTheme.MiniHeadingStyle);

                    using (new EditorGUILayout.VerticalScope(PlayServWindowTheme.LogContainerStyle))
                    {
                        _deployFilesScroll = EditorGUILayout.BeginScrollView(_deployFilesScroll, GUILayout.Height(140));
                        foreach (var file in _deployFilesPreview)
                            EditorGUILayout.LabelField(file, PlayServWindowTheme.LogLineStyle);
                        EditorGUILayout.EndScrollView();
                    }
                }

                if (_deployRunning || !string.IsNullOrWhiteSpace(_deployStatus))
                {
                    GUILayout.Space(6);
                    EditorGUILayout.LabelField("Status", PlayServWindowTheme.MiniHeadingStyle);
                    var deployMessageType = !_deployRunning && _deployStatus.StartsWith("Failed", StringComparison.OrdinalIgnoreCase)
                        ? MessageType.Warning
                        : MessageType.Info;
                    DrawNotice(string.IsNullOrEmpty(_deployStatus) ? "Working..." : _deployStatus, deployMessageType);

                    if (_deployRunning)
                        EditorGUILayout.Slider("Progress", _deployProgress, 0f, 1f);
                }

                if (_versionSyncRunning || !string.IsNullOrWhiteSpace(_versionSyncStatus))
                {
                    GUILayout.Space(6);
                    EditorGUILayout.LabelField("Version Sync", PlayServWindowTheme.MiniHeadingStyle);
                    DrawNotice(_versionSyncStatus, _versionSyncRunning ? MessageType.Info : MessageType.Warning);
                }
            }

            EndSectionCard(expanded);
            EditorPrefs.SetBool(Const.PrefFoldDeployment, _foldDeployment);
        }

        private List<string> BuildDeployFileList(out string error)
        {
            return _deploymentClosureFilter.CollectDeployFiles(
                _deployFolder,
                _deployIncludeSubfolders,
                _deployPattern,
                out error);
        }

        private async Task StartDeployAsync()
        {
            if (_deployRunning || _versionSyncRunning)
                return;

            if (_config == null || _so == null)
            {
                Debug.LogError("[PlayServ] Config is not loaded.");
                return;
            }

            var gameId = _pGameId?.stringValue;
            if (string.IsNullOrWhiteSpace(gameId))
            {
                Debug.LogError("[PlayServ] GameId is empty. Please set it in PlayServ Config.");
                return;
            }

            var files = BuildDeployFileList(out var err);
            if (!string.IsNullOrEmpty(err))
            {
                Debug.LogError($"[PlayServ] {err}");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "Deploy",
                    $"Upload {files.Count} file(s) for GameId '{gameId}'?\n\nEndpoint:\n{ResolveDeployEndpointForDisplay()}",
                    "Deploy",
                    "Cancel"))
            {
                return;
            }

            _deployRunning = true;
            _deployProgress = 0.05f;
            _deployStatus = "Preparing files...";
            _deployCts = new CancellationTokenSource();

            try
            {
                UpdateDeployProgress(_deployStatus, _deployProgress);

                var rootPath = Path.GetFullPath(AssetDatabase.GetAssetPath(_deployFolder));
                await _deploymentUploadAction.ExecuteAsync(
                    _config,
                    gameId,
                    files,
                    rootPath,
                    _deployKeepRelativePaths,
                    UpdateDeployProgress,
                    _deployCts.Token);

                _deployProgress = 1f;
                _deployStatus = "Done.";
                Debug.Log("[PlayServ] Deployment completed successfully.");
            }
            catch (OperationCanceledException)
            {
                _deployStatus = "Cancelled.";
                Debug.LogWarning("[PlayServ] Deployment cancelled.");
            }
            catch (Exception e)
            {
                _deployStatus = $"Failed: {e.Message}";
                Debug.LogError($"[PlayServ] Deployment failed: {e}");
            }
            finally
            {
                _deployRunning = false;
                EditorUtility.ClearProgressBar();

                _deployCts?.Dispose();
                _deployCts = null;
            }
        }

        private async Task StartVersionSyncAsync()
        {
            if (_versionSyncRunning || _deployRunning)
                return;

            if (_config == null || _so == null)
            {
                Debug.LogError("[PlayServ] Config is not loaded.");
                return;
            }

            var gameId = _pGameId?.stringValue;
            if (string.IsNullOrWhiteSpace(gameId))
            {
                Debug.LogError("[PlayServ] GameId is empty. Please set it in PlayServ Config.");
                return;
            }

            var files = BuildDeployFileList(out var err);
            if (!string.IsNullOrEmpty(err))
            {
                Debug.LogError($"[PlayServ] {err}");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "Sync Version",
                    $"Compare local RPC code hash against remote for GameId '{gameId}'?",
                    "Sync",
                    "Cancel"))
            {
                return;
            }

            _versionSyncRunning = true;
            _versionSyncStatus = "Fetching schemas...";
            Repaint();

            try
            {
                var result = await _versionSyncAction.ExecuteAsync(
                    _config,
                    gameId,
                    files,
                    SetVersionSyncStatus,
                    CancellationToken.None);

                if (result.HashesMatch)
                {
                    _so.Update();
                    if (_pGameVersion != null)
                    {
                        _pGameVersion.stringValue = result.LatestVersion;
                        _so.ApplyModifiedProperties();
                        EditorUtility.SetDirty(_config);
                    }

                    _versionSyncStatus = $"Synced to version {result.LatestVersion}.";
                    Debug.Log($"[PlayServ] Version synchronized to {result.LatestVersion}.");
                }
                else
                {
                    _versionSyncStatus = "Hash mismatch. Archive downloaded.";
                    Debug.LogWarning($"[PlayServ] Code hash mismatch. Archive saved to: {result.ArchivePath}");
                }
            }
            catch (Exception e)
            {
                _versionSyncStatus = $"Sync failed: {e.Message}";
                Debug.LogError($"[PlayServ] Version sync failed: {e}");
            }
            finally
            {
                _versionSyncRunning = false;
                Repaint();
            }
        }

        private void UpdateDeployProgress(string status, float progress)
        {
            _deployStatus = status;
            _deployProgress = progress;
            EditorUtility.DisplayProgressBar("PlayServ Deployment", _deployStatus, _deployProgress);
            Repaint();
        }

        private void SetVersionSyncStatus(string status)
        {
            _versionSyncStatus = status;
            Repaint();
        }

        private string ResolveDeployEndpointForDisplay()
        {
            var endpoint = _config?.DeployApiServerAddress?.Trim();
            return PlayServPackageDefaultsProvider.ResolveDeployApiServerAddress(endpoint);
        }

        private void DrawModelFoldout()
        {
            var expanded = BeginSectionCard(
                ref _foldModel,
                "Schema",
                "Model Sync",
                "Check the latest schema, compare timestamps, and regenerate editor-side models from the current source of truth.");

            if (expanded)
            {
                GUILayout.Space(6);

                string currentVersion = EditorPrefs.GetString(Const.PrefKeyJsonSchemaVersion, "");
                string currentTimestampRaw = EditorPrefs.GetString(Const.PrefKeyJsonSchemaTimestamp, "");

                string latestVersion = EditorPrefs.GetString(Const.PrefKeyJsonSchemaLatestVersion, "");
                string latestTimestampRaw = EditorPrefs.GetString(Const.PrefKeyJsonSchemaLatestTimestamp, "");

                static string FormatTimestamp(string raw)
                {
                    if (string.IsNullOrEmpty(raw))
                        return "";

                    if (DateTimeOffset.TryParse(raw, out var dto))
                        return dto.ToLocalTime().DateTime.ToString("yyyy-MM-dd HH:mm:ss");

                    return raw;
                }

                static bool TryParseTimestamp(string raw, out DateTimeOffset value)
                {
                    if (string.IsNullOrEmpty(raw))
                    {
                        value = default;
                        return false;
                    }

                    return DateTimeOffset.TryParse(raw, out value);
                }

                bool hasCurrent = !string.IsNullOrEmpty(currentVersion) || !string.IsNullOrEmpty(currentTimestampRaw);

                bool hasLatest = _showAvailableSchemaInfo &&
                                 (!string.IsNullOrEmpty(latestVersion) || !string.IsNullOrEmpty(latestTimestampRaw));

                bool differs = false;
                bool hasComparableTimestamps = false;
                bool latestIsNewerByTimestamp = false;

                if (hasLatest &&
                    TryParseTimestamp(currentTimestampRaw, out var currentTs) &&
                    TryParseTimestamp(latestTimestampRaw, out var latestTs))
                {
                    hasComparableTimestamps = true;
                    latestIsNewerByTimestamp = latestTs > currentTs;
                }

                if (hasLatest)
                {
                    differs = hasComparableTimestamps
                        ? latestIsNewerByTimestamp
                        : !string.IsNullOrEmpty(latestTimestampRaw) && latestTimestampRaw != currentTimestampRaw;
                }

                string statusMessage = null;
                MessageType? statusType = null;

                if (hasLatest)
                {
                    if (differs)
                    {
                        statusMessage = "A newer schema is available.";
                        statusType = MessageType.Warning;
                    }
                    else
                    {
                        statusMessage = "You have the latest schema already.";
                        statusType = MessageType.Info;
                    }
                }

                if (hasCurrent || hasLatest)
                {
                    if (statusMessage != null && statusType.HasValue)
                        DrawNotice(statusMessage, statusType.Value);

                    EditorGUILayout.LabelField("Schema details", PlayServWindowTheme.MiniHeadingStyle);

                    EditorGUILayout.BeginVertical(PlayServWindowTheme.LogContainerStyle);

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(" ", GUILayout.Width(14));
                    EditorGUILayout.LabelField("Current schema", PlayServWindowTheme.SectionLabelStyle);
                    if (_showAvailableSchemaInfo)
                        EditorGUILayout.LabelField("Latest available schema", PlayServWindowTheme.SectionLabelStyle);
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("V", GUILayout.Width(14));
                    EditorGUILayout.SelectableLabel(
                        string.IsNullOrEmpty(currentVersion) ? "—" : currentVersion,
                        PlayServWindowTheme.InputStyle,
                        GUILayout.Height(StyledFieldHeight));

                    if (_showAvailableSchemaInfo)
                    {
                        EditorGUILayout.SelectableLabel(
                            string.IsNullOrEmpty(latestVersion) ? "—" : latestVersion,
                            PlayServWindowTheme.InputStyle,
                            GUILayout.Height(StyledFieldHeight));
                    }

                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("T", GUILayout.Width(14));
                    EditorGUILayout.SelectableLabel(
                        string.IsNullOrEmpty(currentTimestampRaw) ? "—" : FormatTimestamp(currentTimestampRaw),
                        PlayServWindowTheme.InputStyle,
                        GUILayout.Height(StyledFieldHeight));

                    if (_showAvailableSchemaInfo)
                    {
                        EditorGUILayout.SelectableLabel(
                            string.IsNullOrEmpty(latestTimestampRaw) ? "—" : FormatTimestamp(latestTimestampRaw),
                            PlayServWindowTheme.InputStyle,
                            GUILayout.Height(StyledFieldHeight));
                    }

                    EditorGUILayout.EndHorizontal();

                    if (_showAvailableSchemaInfo)
                    {
                        GUILayout.Space(6);

                        EditorGUILayout.BeginHorizontal();

                        if (DrawActionButton("Hide", PlayServWindowButtonTone.Ghost, GUILayout.Width(120f), GUILayout.Height(28f)))
                            _showAvailableSchemaInfo = false;

                        GUILayout.FlexibleSpace();

                        using (new EditorGUI.DisabledScope(!differs))
                        {
                            if (DrawActionButton("Apply New Schema", PlayServWindowButtonTone.Primary, GUILayout.Width(180f), GUILayout.Height(28f)))
                            {
                                if (EditorUtility.DisplayDialog(
                                        "Apply new schema",
                                        "This will replace the current schema with the latest downloaded one.\nContinue?",
                                        "Apply",
                                        "Cancel"))
                                {
                                    SchemaCodeGenerator.GenerateModels(true);

                                    _showAvailableSchemaInfo = false;
                                    EditorPrefs.DeleteKey(Const.PrefKeyJsonSchemaLatestVersion);
                                    EditorPrefs.DeleteKey(Const.PrefKeyJsonSchemaLatestTimestamp);
                                }
                            }
                        }

                        EditorGUILayout.EndHorizontal();
                    }

                    EditorGUILayout.EndVertical();

                    GUILayout.Space(6);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (DrawActionButton("Check Updates", PlayServWindowButtonTone.Secondary, GUILayout.Width(138f), GUILayout.Height(32f)))
                    {
                        _showAvailableSchemaInfo = true;
                        SchemaLoader.LoadSchema(_pGameAccessToken.stringValue);
                        SchemaLoader.CheckNewSchema();
                    }

                    GUILayout.Space(8f);

                    if (DrawActionButton("Re-generate Models", PlayServWindowButtonTone.Primary, GUILayout.Width(176f), GUILayout.Height(32f)))
                        SchemaCodeGenerator.GenerateModels(false);
                }
            }

            EndSectionCard(expanded);
            EditorPrefs.SetBool(Const.PrefFoldModel, _foldModel);
        }

        private void DrawEventsFoldout()
        {
            var expanded = BeginSectionCard(
                ref _foldEvents,
                "Realtime",
                "Events",
                "Generate the typed events API and keep event payload contracts close to the runtime.");

            if (expanded)
            {
                if (DrawActionButton("Generate Events API", PlayServWindowButtonTone.Primary, GUILayout.Width(168f), GUILayout.Height(32f)))
                    EventsCodeGenerator.Generate();
            }

            EndSectionCard(expanded);
            EditorPrefs.SetBool(Const.PrefFoldEvents, _foldEvents);
        }

        private void DrawCodegenFoldout()
        {
            var expanded = BeginSectionCard(
                ref _foldCodegen,
                "Automation",
                "Code Generation",
                "Generate DTOs on demand and keep the generated layer clean when you need a reset.");

            if (expanded)
            {
                bool autoGen = EditorPrefs.GetBool(Const.PrefKeyAutoCodegen, true);
                bool newAutoGen = EditorGUILayout.ToggleLeft("Enable automatic DTO generation", autoGen);

                if (newAutoGen != autoGen)
                    EditorPrefs.SetBool(Const.PrefKeyAutoCodegen, newAutoGen);

                GUILayout.Space(6);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (DrawActionButton("Generate DTOs Now", PlayServWindowButtonTone.Primary, GUILayout.Width(168f), GUILayout.Height(32f)))
                        SharedCodeGenerator.GenerateMenu();

                    GUILayout.Space(8f);

                    if (DrawActionButton("Remove Generated DTOs", PlayServWindowButtonTone.Danger, GUILayout.Width(184f), GUILayout.Height(32f)))
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

            EndSectionCard(expanded);
            EditorPrefs.SetBool(Const.PrefFoldCodegen, _foldCodegen);
        }

        private void DrawConnectionFoldout()
        {
            var expanded = BeginSectionCard(
                ref _foldConnection,
                "Transport",
                "WebSocket Connection",
                "Low-level socket smoke test for editor diagnostics and message tracing.");

            if (expanded)
            {
                DrawNotice("Test WebSocket connection.", MessageType.Info);

                GUILayout.Space(6);

                EditorGUILayout.LabelField("Connection Settings", PlayServWindowTheme.MiniHeadingStyle);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Endpoint", GUILayout.Width(80));
                string newEndpoint = EditorGUILayout.TextField(_wsEndpoint, PlayServWindowTheme.InputStyle);
                if (newEndpoint != _wsEndpoint)
                {
                    _wsEndpoint = newEndpoint;
                    EditorPrefs.SetString(Const.PrefKeyWebSocketEndpoint, _wsEndpoint);
                }
                EditorGUILayout.EndHorizontal();

                GUILayout.Space(6);

                bool isConnected = _wsTransport != null && _wsTransport.IsConnected;
                bool isConnecting = _wsTransport != null && _wsTransport.IsConnecting;

                EditorGUILayout.BeginHorizontal();

                using (new EditorGUI.DisabledScope(isConnected || isConnecting))
                {
                    if (DrawActionButton(isConnecting ? "Connecting..." : "Connect", PlayServWindowButtonTone.Primary, GUILayout.Height(30f)))
                        _ = ConnectWebSocket();
                }

                using (new EditorGUI.DisabledScope(!isConnected))
                {
                    if (DrawActionButton("Disconnect", PlayServWindowButtonTone.Secondary, GUILayout.Height(30f)))
                        DisconnectWebSocket();
                }

                EditorGUILayout.EndHorizontal();

                GUILayout.Space(6);

                EditorGUILayout.LabelField("Status", PlayServWindowTheme.MiniHeadingStyle);
                string statusText = isConnected ? "Connected" : isConnecting ? "Connecting..." : "Disconnected";
                var statusColor = isConnected ? Color.green : isConnecting ? Color.yellow : Color.gray;

                var previousColor = GUI.color;
                GUI.color = statusColor;
                EditorGUILayout.LabelField("● " + statusText, PlayServWindowTheme.StatusValueStyle);
                GUI.color = previousColor;

                GUILayout.Space(6);

                using (new EditorGUI.DisabledScope(!isConnected))
                {
                    EditorGUILayout.LabelField("Send Test Message", PlayServWindowTheme.MiniHeadingStyle);
                    _testMessage = EditorGUILayout.TextArea(_testMessage, PlayServWindowTheme.TextAreaStyle, GUILayout.Height(64f));

                    if (DrawActionButton("Send Message", PlayServWindowButtonTone.Primary, GUILayout.Width(132f), GUILayout.Height(28f)))
                        _ = SendTestMessage();
                }

                GUILayout.Space(6);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Connection Log", PlayServWindowTheme.MiniHeadingStyle);
                if (DrawActionButton("Clear", PlayServWindowButtonTone.Ghost, GUILayout.Width(74f), GUILayout.Height(24f)))
                {
                    _wsTransport?.ClearLogs();
                    Repaint();
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginVertical(PlayServWindowTheme.LogContainerStyle);
                _connectionScrollPos = EditorGUILayout.BeginScrollView(_connectionScrollPos, GUILayout.Height(200));

                if (_wsTransport != null && _wsTransport.LogMessages.Count > 0)
                {
                    foreach (var log in _wsTransport.LogMessages)
                        EditorGUILayout.SelectableLabel(log, PlayServWindowTheme.LogLineStyle, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                }
                else
                {
                    EditorGUILayout.LabelField("No logs yet...", PlayServWindowTheme.EmptyStateStyle);
                }

                EditorGUILayout.EndScrollView();
                EditorGUILayout.EndVertical();
            }

            EndSectionCard(expanded);
            EditorPrefs.SetBool(Const.PrefFoldConnection, _foldConnection);
        }

        private async Task ConnectWebSocket()
        {
            if (string.IsNullOrWhiteSpace(_wsEndpoint))
            {
                EditorUtility.DisplayDialog("Error", "Endpoint cannot be empty", "OK");
                return;
            }

            try
            {
                _wsTransport?.Dispose();
                _wsTransport = new EditorWebSocketTransport(_wsEndpoint);

                _wsTransport.OnConnected += () => Repaint();
                _wsTransport.OnMessageReceived += _ => Repaint();
                _wsTransport.OnError += _ => Repaint();
                _wsTransport.OnDisconnected += () => Repaint();

                Repaint();
                await _wsTransport.ConnectAsync();
                Repaint();
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Connection Error", ex.Message, "OK");
            }
        }

        private void DisconnectWebSocket()
        {
            _wsTransport?.Disconnect();
            Repaint();
        }

        private async Task SendTestMessage()
        {
            if (_wsTransport == null || !_wsTransport.IsConnected)
                return;

            await _wsTransport.SendAsync(_testMessage);
            Repaint();
        }
    }
}
#endif
