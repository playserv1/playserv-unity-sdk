#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Playserv.Wrapper;
using Playserv.CodeGenerator.Editor;
using Playserv.Deploy.Editor.Analysis;
using Playserv.Deploy.Editor;
using Playserv.Events.Editor;
using Playserv.ModelGenerator.Editor;
using Playserv.Editor.Proxy;

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
        private const string ClientProjectSettingsBridgeTypeName = "Playserv.ClientEditor.PlayServProjectSettingsBridge";
        private const string DrawProjectConfigUiMethodName = "DrawProjectConfigUi";
        private static readonly string[] AllowedDeployUsingNamespaces =
        {
            "System",
            "System.Collections.Generic",
            "System.Linq",
            "System.Text"
        };
        private const bool ShowWebSocketConnectionMenu = false;

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
        private List<string> _deployFilesPreview = new();

        private bool _deployRunning;
        private float _deployProgress;
        private string _deployStatus = "";
        private CancellationTokenSource _deployCts;
        private bool _versionSyncRunning;
        private string _versionSyncStatus = "";

        private enum ButtonTone
        {
            Primary,
            Secondary,
            Ghost,
            Danger
        }

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

            var wnd = GetWindow<PlayServWindow>(title: "PlayServ");
            wnd.titleContent = new GUIContent(BuildWindowTitle(PlayServConfigProvider.FindExisting()?.SdkVersion));
            wnd.minSize = new Vector2(MinWindowWidth, 760f);
            if (wnd.position.width > DefaultWindowWidth)
                wnd.position = new Rect(wnd.position.x, wnd.position.y, DefaultWindowWidth, Math.Max(wnd.position.height, 760f));
            wnd.Show();
            wnd.Focus();
        }

        private void OnEnable()
        {
            _foldCodegen = EditorPrefs.GetBool(Const.PrefFoldCodegen, true);
            _foldConfig = EditorPrefs.GetBool(Const.PrefFoldConfig, true);
            _foldConnection = EditorPrefs.GetBool(Const.PrefFoldConnection, false);
            _foldDeployment = EditorPrefs.GetBool(Const.PrefFoldDeployment, false);

            _showAvailableSchemaInfo = false;

            if (position.width > DefaultWindowWidth)
                position = new Rect(position.x, position.y, DefaultWindowWidth, Math.Max(position.height, 760f));

            _wsEndpoint = EditorPrefs.GetString(
                Const.PrefKeyWebSocketEndpoint,
                PlayServPackageDefaultsProvider.ResolveBackendServerAddress(null)
            );

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

                if (DrawActionButton(expanded ? "Collapse" : "Expand", ButtonTone.Ghost, GUILayout.Width(92f), GUILayout.Height(24f)))
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

        private bool DrawActionButton(string label, ButtonTone tone, params GUILayoutOption[] options)
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
                    if (DrawActionButton("Generate DTOs Now", ButtonTone.Primary, GUILayout.Width(168f), GUILayout.Height(32f)))
                        SharedCodeGenerator.GenerateMenu();

                    GUILayout.Space(8f);

                    if (DrawActionButton("Remove Generated DTOs", ButtonTone.Danger, GUILayout.Width(184f), GUILayout.Height(32f)))
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

        private void DrawEventsFoldout()
        {
            var expanded = BeginSectionCard(
                ref _foldEvents,
                "Realtime",
                "Events",
                "Generate the typed events API and keep event payload contracts close to the runtime.");

            if (expanded)
            {
                if (DrawActionButton("Generate Events API", ButtonTone.Primary, GUILayout.Width(168f), GUILayout.Height(32f)))
                    EventsCodeGenerator.Generate();
            }

            EndSectionCard(expanded);
            EditorPrefs.SetBool(Const.PrefFoldEvents, _foldEvents);
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

                        if (DrawActionButton("Hide", ButtonTone.Ghost, GUILayout.Width(120f), GUILayout.Height(28f)))
                            _showAvailableSchemaInfo = false;

                        GUILayout.FlexibleSpace();

                        using (new EditorGUI.DisabledScope(!differs))
                        {
                            if (DrawActionButton("Apply New Schema", ButtonTone.Primary, GUILayout.Width(180f), GUILayout.Height(28f)))
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
                    if (DrawActionButton("Check Updates", ButtonTone.Secondary, GUILayout.Width(138f), GUILayout.Height(32f)))
                    {
                        _showAvailableSchemaInfo = true;
                        SchemaLoader.LoadSchema(_pGameAccessToken.stringValue);
                        SchemaLoader.CheckNewSchema();
                    }

                    GUILayout.Space(8f);

                    if (DrawActionButton("Re-generate Models", ButtonTone.Primary, GUILayout.Width(176f), GUILayout.Height(32f)))
                        SchemaCodeGenerator.GenerateModels(false);
                }
            }

            EndSectionCard(expanded);
            EditorPrefs.SetBool(Const.PrefFoldModel, _foldModel);
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
                    if (DrawActionButton(isConnecting ? "Connecting..." : "Connect", ButtonTone.Primary, GUILayout.Height(30f)))
                        _ = ConnectWebSocket();
                }

                using (new EditorGUI.DisabledScope(!isConnected))
                {
                    if (DrawActionButton("Disconnect", ButtonTone.Secondary, GUILayout.Height(30f)))
                        DisconnectWebSocket();
                }

                EditorGUILayout.EndHorizontal();

                GUILayout.Space(6);

                EditorGUILayout.LabelField("Status", PlayServWindowTheme.MiniHeadingStyle);
                string statusText = isConnected ? "Connected" : isConnecting ? "Connecting..." : "Disconnected";
                var statusColor = isConnected ? Color.green : isConnecting ? Color.yellow : Color.gray;

                var prevColor = GUI.color;
                GUI.color = statusColor;
                EditorGUILayout.LabelField("● " + statusText, PlayServWindowTheme.StatusValueStyle);
                GUI.color = prevColor;

                GUILayout.Space(6);

                using (new EditorGUI.DisabledScope(!isConnected))
                {
                    EditorGUILayout.LabelField("Send Test Message", PlayServWindowTheme.MiniHeadingStyle);
                    _testMessage = EditorGUILayout.TextArea(_testMessage, PlayServWindowTheme.TextAreaStyle, GUILayout.Height(64f));

                    if (DrawActionButton("Send Message", ButtonTone.Primary, GUILayout.Width(132f), GUILayout.Height(28f)))
                        _ = SendTestMessage();
                }

                GUILayout.Space(6);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Connection Log", PlayServWindowTheme.MiniHeadingStyle);
                if (DrawActionButton("Clear", ButtonTone.Ghost, GUILayout.Width(74f), GUILayout.Height(24f)))
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

                using (new EditorGUI.DisabledScope(_deployRunning || _versionSyncRunning))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (DrawActionButton("Preview Files", ButtonTone.Secondary, GUILayout.Width(120f), GUILayout.Height(30f)))
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

                        if (DrawActionButton("Clear Preview", ButtonTone.Ghost, GUILayout.Width(120f), GUILayout.Height(30f)))
                        {
                            _deployFilesPreview.Clear();
                            _deployShowFileList = false;
                        }
                    }

                    GUILayout.Space(8f);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (DrawActionButton("Sync Version", ButtonTone.Secondary, GUILayout.Width(120f), GUILayout.Height(30f)))
                            _ = StartVersionSyncAsync();

                        GUILayout.Space(8f);

                        if (DrawActionButton("Deploy Now", ButtonTone.Primary, GUILayout.Width(140f), GUILayout.Height(30f)))
                            _ = StartDeployAsync();
                    }
                }

                using (new EditorGUI.DisabledScope(!_deployRunning))
                {
                    if (DrawActionButton("Cancel", ButtonTone.Danger, GUILayout.Width(120f), GUILayout.Height(28f)))
                        _deployCts?.Cancel();
                }

                if (_deployShowFileList && _deployFilesPreview.Count > 0)
                {
                    GUILayout.Space(6);
                    EditorGUILayout.LabelField($"Files ({_deployFilesPreview.Count})", PlayServWindowTheme.MiniHeadingStyle);

                    using (new EditorGUILayout.VerticalScope(PlayServWindowTheme.LogContainerStyle))
                    {
                        _deployFilesScroll = EditorGUILayout.BeginScrollView(_deployFilesScroll, GUILayout.Height(140));
                        foreach (var f in _deployFilesPreview.Take(300))
                            EditorGUILayout.LabelField(f, PlayServWindowTheme.LogLineStyle);
                        if (_deployFilesPreview.Count > 300)
                            EditorGUILayout.LabelField($"...and {_deployFilesPreview.Count - 300} more", PlayServWindowTheme.EmptyStateStyle);
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
            error = null;

            if (_deployFolder == null)
            {
                error = "Please select a Folder to deploy.";
                return new List<string>();
            }

            var folderPath = AssetDatabase.GetAssetPath(_deployFolder);
            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                error = "Selected asset is not a folder.";
                return new List<string>();
            }

            var absoluteFolderPath = Path.GetFullPath(folderPath);

            var option = _deployIncludeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            var pattern = string.IsNullOrWhiteSpace(_deployPattern) ? "*" : _deployPattern.Trim();

            string[] files;
            try
            {
                files = Directory.GetFiles(absoluteFolderPath, pattern, option);
            }
            catch (Exception e)
            {
                error = $"Failed to list files: {e.Message}";
                return new List<string>();
            }

            var list = files
                .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var candidateFiles = list;

            AnalysisResult analysisResult;
            try
            {
                var analyzer = new FunctionAnalyzerService();
                analysisResult = analyzer.AnalyzeFiles(candidateFiles);
            }
            catch (Exception ex)
            {
                error = $"Failed to analyze files: {ex.Message}";
                return new List<string>();
            }

            if (!analysisResult.Success)
            {
                var distinctErrors = analysisResult.Errors
                    .Where(e => !string.IsNullOrWhiteSpace(e))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                var errorsToShow = distinctErrors.Take(20).ToList();
                var hiddenErrorsCount = Math.Max(0, distinctErrors.Count - errorsToShow.Count);

                var details = errorsToShow.Count == 0
                    ? "  - Unknown validation error."
                    : string.Join(Environment.NewLine, errorsToShow.Select(e => $"  - {e}"));

                if (hiddenErrorsCount > 0)
                    details += Environment.NewLine + $"  - ...and {hiddenErrorsCount} more";

                error = "Code analysis failed:" + Environment.NewLine + details;
                return new List<string>();
            }

            var analyzedFileSet = new HashSet<string>(
                analysisResult.FilesToCompile.Where(f => !string.IsNullOrWhiteSpace(f)),
                StringComparer.OrdinalIgnoreCase);

            list = candidateFiles
                .Where(analyzedFileSet.Contains)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var excludedByAnalysis = candidateFiles
                .Where(f => !analyzedFileSet.Contains(f))
                .ToList();

            if (excludedByAnalysis.Count > 0)
            {
                Debug.LogWarning(
                    $"[PlayServ] Excluded {excludedByAnalysis.Count} file(s) not required by analyzer RPC dependency closure: " +
                    string.Join(", ", excludedByAnalysis.Take(15)));
            }

            if (list.Count == 0)
                error = "No files matched the current pattern.";
            else
                Debug.Log($"[PlayServ] Final deploy file count: {list.Count}. Files: {string.Join(", ", list)}");

            return list;
        }

        private async Task StartDeployAsync()
        {
            if (_deployRunning || _versionSyncRunning)
                return;

            if (_config == null)
            {
                Debug.LogError("[PlayServ] Please assign DeploymentSettings asset.");
                return;
            }

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
                EditorUtility.DisplayProgressBar("PlayServ Deployment", _deployStatus, _deployProgress);
                var api = new DeploymentApiClient(_config);

                if (_deployKeepRelativePaths)
                {
                    try
                    {
                        await DeployWithRelativePathsAsync(api, gameId, files, _deployFolder, _deployCts.Token);
                    }
                    catch (InvalidOperationException e) when (IsNativeAotFailure(e) || IsInvalidZipUploadFailure(e))
                    {
                        Debug.LogWarning(
                            "[PlayServ] Relative-path ZIP upload failed. Retrying with flat ZIP packaging.");

                        var service = new DeploymentService(api);
                        await service.DeployAsync(gameId, files, _deployCts.Token);
                    }
                }
                else
                {
                    var service = new DeploymentService(api);
                    await service.DeployAsync(gameId, files, _deployCts.Token);
                }

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
                var api = new DeploymentApiClient(_config);

                await api.FetchLatestSchemasAsync(gameId);
                _versionSyncStatus = "Computing local hash...";
                Repaint();

                var localHash = ComputeCodeHash(CreateZipArchiveBytes(files));

                _versionSyncStatus = "Fetching remote hash...";
                Repaint();

                var remoteHash = await api.GetRemoteCodeHashAsync(gameId);

                if (string.Equals(localHash, remoteHash, StringComparison.OrdinalIgnoreCase))
                {
                    _versionSyncStatus = "Hashes match. Fetching latest version...";
                    Repaint();

                    var latestVersion = await api.GetLatestVersionAsync(gameId);

                    _so.Update();
                    if (_pGameVersion != null)
                    {
                        _pGameVersion.stringValue = latestVersion;
                        _so.ApplyModifiedProperties();
                        EditorUtility.SetDirty(_config);
                    }

                    _versionSyncStatus = $"Synced to version {latestVersion}.";
                    Debug.Log($"[PlayServ] Version synchronized to {latestVersion}.");
                }
                else
                {
                    _versionSyncStatus = "Hash mismatch. Downloading archive...";
                    Repaint();

                    var archiveOutputDir = Path.Combine(Path.GetTempPath(), "playserv-sync");
                    var archivePath = await api.DownloadCodeArchiveAsync(gameId, archiveOutputDir);

                    _versionSyncStatus = "Hash mismatch. Archive downloaded.";
                    Debug.LogWarning($"[PlayServ] Code hash mismatch. Archive saved to: {archivePath}");
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

        private static byte[] CreateZipArchiveBytes(List<string> filePaths)
        {
            using var memoryStream = new MemoryStream();

            using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var filePath in filePaths)
                {
                    if (!File.Exists(filePath))
                        continue;

                    archive.CreateEntryFromFile(filePath, Path.GetFileName(filePath));
                }
            }

            return memoryStream.ToArray();
        }

        private static string ComputeCodeHash(byte[] zipBytes)
        {
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(zipBytes);
            return BitConverter.ToString(hashBytes).Replace("-", string.Empty);
        }

        private string ResolveDeployEndpointForDisplay()
        {
            var endpoint = _config?.DeployApiServerAddress?.Trim();
            return PlayServPackageDefaultsProvider.ResolveDeployApiServerAddress(endpoint);
        }

        private async Task DeployWithRelativePathsAsync(
            DeploymentApiClient api,
            string gameId,
            List<string> absoluteFiles,
            DefaultAsset rootFolderAsset,
            CancellationToken ct)
        {
            var rootPath = Path.GetFullPath(AssetDatabase.GetAssetPath(rootFolderAsset));

            _deployStatus = "Creating ZIP...";
            _deployProgress = 0.15f;
            EditorUtility.DisplayProgressBar("PlayServ Deployment", _deployStatus, _deployProgress);

            var zipPath = CreateZipWithRelativePaths(rootPath, absoluteFiles);

            try
            {
                _deployStatus = "Uploading ZIP...";
                _deployProgress = 0.55f;
                EditorUtility.DisplayProgressBar("PlayServ Deployment", _deployStatus, _deployProgress);

                await api.UploadDeploymentAsync(gameId, zipPath, ct);

                _deployStatus = "Upload finished.";
                _deployProgress = 0.95f;
                EditorUtility.DisplayProgressBar("PlayServ Deployment", _deployStatus, _deployProgress);
            }
            finally
            {
                TryDeleteTemp(zipPath);
            }
        }

        private static string CreateZipWithRelativePaths(string rootFolderPath, List<string> absoluteFiles)
        {
            // Build zip in temp folder
            var zipPath = Path.Combine(Path.GetTempPath(), $"playserv_deploy_{Guid.NewGuid():N}.zip");

            Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);

            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                var root = rootFolderPath.Replace('\\', '/').TrimEnd('/');

                foreach (var absFile in absoluteFiles)
                {
                    if (!File.Exists(absFile))
                        continue;

                    var normalized = absFile.Replace('\\', '/');

                    // Make relative entry name
                    var entry = normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                        ? normalized.Substring(root.Length).TrimStart('/')
                        : Path.GetFileName(absFile);

                    if (string.IsNullOrWhiteSpace(entry))
                        entry = Path.GetFileName(absFile);

                    archive.CreateEntryFromFile(absFile, entry);
                }
            }

            return zipPath;
        }

        private static void TryDeleteTemp(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PlayServ] Failed to delete temp zip: {e.Message}");
            }
        }

        private static bool IsNativeAotFailure(Exception exception)
        {
            if (exception == null)
                return false;

            var message = exception.Message ?? string.Empty;
            return message.IndexOf("Native AOT compilation failed", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsInvalidZipUploadFailure(Exception exception)
        {
            if (exception == null)
                return false;

            var message = exception.Message ?? string.Empty;
            return message.IndexOf("Request body is empty", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("valid ZIP archive", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static List<string> FilterFilesWithUnsupportedUsingNamespaces(
            List<string> files,
            out List<string> excludedFiles)
        {
            excludedFiles = new List<string>();
            var validFiles = new List<string>(files.Count);

            foreach (var file in files)
            {
                if (HasUnsupportedUsingNamespace(file))
                {
                    excludedFiles.Add(file);
                    continue;
                }

                validFiles.Add(file);
            }

            return validFiles;
        }

        private static bool HasUnsupportedUsingNamespace(string filePath)
        {
            string text;
            try
            {
                text = File.ReadAllText(filePath);
            }
            catch
            {
                return true;
            }

            var usingMatches = Regex.Matches(text, @"^\s*using\s+([^;]+);", RegexOptions.Multiline);
            foreach (Match match in usingMatches)
            {
                var rawTarget = match.Groups[1].Value.Trim();
                if (string.IsNullOrWhiteSpace(rawTarget))
                    continue;

                if (rawTarget.StartsWith("static ", StringComparison.Ordinal))
                    rawTarget = rawTarget.Substring("static ".Length).Trim();

                var aliasIndex = rawTarget.IndexOf('=');
                if (aliasIndex >= 0 && aliasIndex < rawTarget.Length - 1)
                    rawTarget = rawTarget.Substring(aliasIndex + 1).Trim();

                if (rawTarget.StartsWith("global::", StringComparison.Ordinal))
                    rawTarget = rawTarget.Substring("global::".Length);

                if (!IsAllowedDeployUsingNamespace(rawTarget))
                    return true;
            }

            return false;
        }

        private static bool IsAllowedDeployUsingNamespace(string namespaceName)
        {
            foreach (var allowed in AllowedDeployUsingNamespaces)
            {
                if (string.Equals(namespaceName, allowed, StringComparison.Ordinal) ||
                    namespaceName.StartsWith(allowed + ".", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ValidateRpcConstructors(List<string> files, out string error)
        {
            error = null;
            foreach (var file in files)
            {
                string text;
                try
                {
                    text = File.ReadAllText(file);
                }
                catch (Exception e)
                {
                    error = $"Failed to read '{file}': {e.Message}";
                    return false;
                }

                if (text.IndexOf("[Rpc]", StringComparison.Ordinal) < 0)
                    continue;

                var classMatch = Regex.Match(text, @"class\s+([A-Za-z_][A-Za-z0-9_]*)");
                if (!classMatch.Success)
                    continue;

                var className = classMatch.Groups[1].Value;
                var ctorPattern = @"\b" + Regex.Escape(className) + @"\s*\(([^)]*)\)";
                var ctorMatches = Regex.Matches(text, ctorPattern);
                var constructorCount = ctorMatches.Count;

                if (constructorCount > 1)
                {
                    error = $"RPC class '{className}' in '{file}' cannot define multiple constructors.";
                    return false;
                }
            }

            return true;
        }

        private static List<string> ReduceToRpcDependencyClosure(List<string> files, out List<string> excludedFiles)
        {
            excludedFiles = new List<string>();
            if (files == null || files.Count == 0)
                return new List<string>();

            var contentByFile = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var typesByFile = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var fileByType = new Dictionary<string, string>(StringComparer.Ordinal);
            var rpcRootFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in files)
            {
                string text;
                try
                {
                    text = File.ReadAllText(file);
                }
                catch
                {
                    continue;
                }

                contentByFile[file] = text;
                if (ContainsRpcAttribute(text))
                    rpcRootFiles.Add(file);

                var declaredTypes = ExtractDeclaredTypes(text);
                typesByFile[file] = declaredTypes;

                foreach (var typeName in declaredTypes)
                {
                    if (!fileByType.ContainsKey(typeName))
                        fileByType[typeName] = file;
                }
            }

            if (rpcRootFiles.Count == 0)
                return files.ToList();

            var dependencyGraph = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                if (!contentByFile.TryGetValue(file, out var text))
                    continue;

                var dependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var typeName in fileByType.Keys)
                {
                    if (typesByFile.TryGetValue(file, out var ownTypes) && ownTypes.Contains(typeName))
                        continue;

                    if (ContainsTypeReference(text, typeName))
                    {
                        var depFile = fileByType[typeName];
                        if (!string.Equals(depFile, file, StringComparison.OrdinalIgnoreCase))
                            dependencies.Add(depFile);
                    }
                }

                dependencyGraph[file] = dependencies;
            }

            var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>(rpcRootFiles);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!selected.Add(current))
                    continue;

                if (!dependencyGraph.TryGetValue(current, out var deps))
                    continue;

                foreach (var dep in deps)
                {
                    if (!selected.Contains(dep))
                        queue.Enqueue(dep);
                }
            }

            var result = files.Where(f => selected.Contains(f)).ToList();
            excludedFiles = files.Where(f => !selected.Contains(f)).ToList();
            return result.Count > 0 ? result : files.ToList();
        }

        private static bool ContainsRpcAttribute(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return Regex.IsMatch(text, @"\[\s*Rpc(\s*\(|\s*\])", RegexOptions.Multiline);
        }

        private static HashSet<string> ExtractDeclaredTypes(string text)
        {
            var types = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(text))
                return types;

            var matches = Regex.Matches(text, @"\b(class|struct|interface|enum|record)\s+([A-Za-z_][A-Za-z0-9_]*)");
            foreach (Match match in matches)
            {
                var typeName = match.Groups[2].Value;
                if (!string.IsNullOrWhiteSpace(typeName))
                    types.Add(typeName);
            }

            return types;
        }

        private static bool ContainsTypeReference(string text, string typeName)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(typeName))
                return false;

            var pattern = $@"\b{Regex.Escape(typeName)}\b";
            return Regex.IsMatch(text, pattern);
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
                    if (DrawActionButton("Create / Locate Config", ButtonTone.Primary, GUILayout.Width(176f), GUILayout.Height(32f)))
                        EnsureConfig();

                    DrawNotice("Config asset not found.", MessageType.Warning);
                }
                else
                {
                    if (TryDrawClientProjectConfigUi(out var configUiChanged))
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
                    if (DrawActionButton("Ping", ButtonTone.Secondary, GUILayout.Width(72f), GUILayout.Height(24f)))
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

        private static bool TryDrawClientProjectConfigUi(out bool changed)
        {
            changed = false;

            if (!TryGetClientProjectConfigUiMethod(out var drawMethod))
                return false;

            try
            {
                var args = new object[] { false };
                var result = drawMethod.Invoke(null, args);
                changed = args[0] is bool hasChanged && hasChanged;
                return result is bool drawn && drawn;
            }
            catch
            {
                changed = false;
                return false;
            }
        }

        private static bool TryGetClientProjectConfigUiMethod(out MethodInfo drawMethod)
        {
            drawMethod = null;

            var bridgeType = FindClientProjectSettingsBridgeType();
            if (bridgeType == null)
                return false;

            drawMethod = bridgeType.GetMethod(
                DrawProjectConfigUiMethodName,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            return drawMethod != null;
        }

        private static Type FindClientProjectSettingsBridgeType()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(ClientProjectSettingsBridgeTypeName, throwOnError: false);
                if (type != null)
                    return type;
            }

            return null;
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

                    if (DrawActionButton("Ping Config", ButtonTone.Secondary, GUILayout.Width(116f), GUILayout.Height(28f)))
                        FocusConfigAsset();

                    GUILayout.Space(8f);

                    if (DrawActionButton("Open Docs", ButtonTone.Secondary, GUILayout.Width(112f), GUILayout.Height(28f)))
                        Application.OpenURL(DocsUrl);
                }
            }
        }

        private static class PlayServWindowTheme
        {
            public static readonly Color Background = Parse("#0E1117");
            public static readonly Color GridLine = new Color(1f, 1f, 1f, 0.08f);

            public static GUIStyle TopBarStyle { get; private set; }
            public static GUIStyle TopBarTitleStyle { get; private set; }
            public static GUIStyle TopBarNavStyle { get; private set; }
            public static GUIStyle HeroCardStyle { get; private set; }
            public static GUIStyle HeroTitleStyle { get; private set; }
            public static GUIStyle HeroAccentStyle { get; private set; }
            public static GUIStyle HeroBodyStyle { get; private set; }
            public static GUIStyle PillStyle { get; private set; }
            public static GUIStyle MetricCardStyle { get; private set; }
            public static GUIStyle MetricLabelStyle { get; private set; }
            public static GUIStyle MetricValueStyle { get; private set; }
            public static GUIStyle MetricCaptionStyle { get; private set; }
            public static GUIStyle CardStyle { get; private set; }
            public static GUIStyle CardBodyStyle { get; private set; }
            public static GUIStyle SectionPillStyle { get; private set; }
            public static GUIStyle SectionTitleButtonStyle { get; private set; }
            public static GUIStyle SectionSubtitleStyle { get; private set; }
            public static GUIStyle MiniHeadingStyle { get; private set; }
            public static GUIStyle SectionLabelStyle { get; private set; }
            public static GUIStyle StatusValueStyle { get; private set; }
            public static GUIStyle InputStyle { get; private set; }
            public static GUIStyle TextAreaStyle { get; private set; }
            public static GUIStyle NoticeInfoStyle { get; private set; }
            public static GUIStyle NoticeWarningStyle { get; private set; }
            public static GUIStyle LogContainerStyle { get; private set; }
            public static GUIStyle LogLineStyle { get; private set; }
            public static GUIStyle EmptyStateStyle { get; private set; }
            public static GUIStyle FooterCardStyle { get; private set; }
            public static GUIStyle FooterTitleStyle { get; private set; }
            public static GUIStyle FooterBodyStyle { get; private set; }

            private static GUIStyle _primaryButtonStyle;
            private static GUIStyle _secondaryButtonStyle;
            private static GUIStyle _ghostButtonStyle;
            private static GUIStyle _dangerButtonStyle;
            private static Texture2D _transparentTexture;

            public static void Ensure()
            {
                if (TopBarStyle != null)
                    return;

                TopBarStyle = CreateBoxStyle("#12161D", "#262C36", new RectOffset(16, 16, 10, 10), new RectOffset(0, 0, 0, 0));
                HeroCardStyle = CreateBoxStyle("#12161D", "#262C36", new RectOffset(18, 18, 18, 18), new RectOffset(0, 0, 0, 0));
                MetricCardStyle = CreateBoxStyle("#12161D", "#262C36", new RectOffset(14, 14, 12, 12), new RectOffset(0, 0, 0, 0));
                CardStyle = CreateBoxStyle("#12161D", "#262C36", new RectOffset(18, 18, 14, 14), new RectOffset(0, 0, 0, 0));
                CardBodyStyle = CreateBoxStyle("#0F1319", "#1E232B", new RectOffset(14, 14, 14, 14), new RectOffset(0, 0, 0, 0));
                FooterCardStyle = CreateBoxStyle("#12161D", "#262C36", new RectOffset(18, 18, 16, 16), new RectOffset(0, 0, 0, 0));
                LogContainerStyle = CreateBoxStyle("#0F1319", "#1E232B", new RectOffset(12, 12, 10, 10), new RectOffset(0, 0, 0, 0));
                NoticeInfoStyle = CreateBoxStyle("#141B26", "#293446", new RectOffset(12, 12, 10, 10), new RectOffset(0, 0, 0, 0));
                NoticeWarningStyle = CreateBoxStyle("#241C17", "#4A392A", new RectOffset(12, 12, 10, 10), new RectOffset(0, 0, 0, 0));

                PillStyle = CreateChipStyle("#171B22", "#2A303A", "#C3CBD8", 11, FontStyle.Bold, new RectOffset(10, 10, 5, 5));
                SectionPillStyle = CreateChipStyle("#171B22", "#2A303A", "#8CB2FF", 10, FontStyle.Bold, new RectOffset(8, 8, 4, 4));

                TopBarTitleStyle = CreateLabelStyle(16, FontStyle.Bold, "#F3F5F8");
                TopBarNavStyle = CreateLabelStyle(12, FontStyle.Normal, "#8892A0");

                HeroTitleStyle = CreateWrappedLabelStyle(30, FontStyle.Bold, "#F3F5F8");
                HeroAccentStyle = CreateWrappedLabelStyle(18, FontStyle.Bold, "#A8D3FF");
                HeroBodyStyle = CreateWrappedLabelStyle(13, FontStyle.Normal, "#AAB2BF");

                MetricLabelStyle = CreateLabelStyle(11, FontStyle.Bold, "#8892A0");
                MetricValueStyle = CreateWrappedLabelStyle(14, FontStyle.Bold, "#F3F5F8");
                MetricCaptionStyle = CreateWrappedLabelStyle(11, FontStyle.Normal, "#7D8693");

                SectionTitleButtonStyle = new GUIStyle(EditorStyles.label)
                {
                    fontSize = 18,
                    fontStyle = FontStyle.Bold,
                    wordWrap = true,
                    stretchWidth = true,
                    alignment = TextAnchor.MiddleLeft,
                    padding = new RectOffset(0, 0, 0, 0),
                    margin = new RectOffset(0, 0, 0, 0),
                    normal = { textColor = Parse("#F6F7FF"), background = TransparentTexture },
                    hover = { textColor = Parse("#8BC8FF"), background = TransparentTexture }
                };

                SectionSubtitleStyle = CreateWrappedLabelStyle(12, FontStyle.Normal, "#8D97A5");
                MiniHeadingStyle = CreateLabelStyle(11, FontStyle.Bold, "#AAB3BF");
                SectionLabelStyle = CreateLabelStyle(11, FontStyle.Bold, "#C3CBD8");
                StatusValueStyle = CreateLabelStyle(12, FontStyle.Bold, "#F3F5F8");
                LogLineStyle = CreateWrappedLabelStyle(11, FontStyle.Normal, "#B3BBC7");
                EmptyStateStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 11,
                    normal = { textColor = Parse("#7F88A6") }
                };

                FooterTitleStyle = CreateLabelStyle(14, FontStyle.Bold, "#F3F5F8");
                FooterBodyStyle = CreateWrappedLabelStyle(12, FontStyle.Normal, "#97A0AD");
                InputStyle = CreateInputStyle("#0F1319", "#262C36", "#E9EEFF");
                TextAreaStyle = CreateTextAreaStyle("#0F1319", "#262C36", "#E9EEFF");

                _primaryButtonStyle = CreateButtonStyle("#6E56CF", "#7B63DB", "#F8F7FF", 12, FontStyle.Bold);
                _secondaryButtonStyle = CreateButtonStyle("#1A1F27", "#202632", "#F0F3FF", 12, FontStyle.Normal);
                _ghostButtonStyle = CreateButtonStyle("#12161D", "#191E26", "#AAB4C0", 12, FontStyle.Normal);
                _dangerButtonStyle = CreateButtonStyle("#362126", "#41282E", "#FFD7E6", 12, FontStyle.Bold);
            }

            public static GUIStyle GetButtonStyle(ButtonTone tone)
            {
                Ensure();
                switch (tone)
                {
                    case ButtonTone.Primary:
                        return _primaryButtonStyle;
                    case ButtonTone.Ghost:
                        return _ghostButtonStyle;
                    case ButtonTone.Danger:
                        return _dangerButtonStyle;
                    default:
                        return _secondaryButtonStyle;
                }
            }

            private static Texture2D TransparentTexture
            {
                get
                {
                    if (_transparentTexture != null)
                        return _transparentTexture;

                    _transparentTexture = new Texture2D(1, 1);
                    _transparentTexture.SetPixel(0, 0, new Color(0f, 0f, 0f, 0f));
                    _transparentTexture.Apply();
                    _transparentTexture.hideFlags = HideFlags.HideAndDontSave;
                    return _transparentTexture;
                }
            }

            private static GUIStyle CreateBoxStyle(string fillHex, string borderHex, RectOffset padding, RectOffset margin)
            {
                var texture = CreateBorderedTexture(Parse(fillHex), Parse(borderHex));
                return new GUIStyle(GUI.skin.box)
                {
                    normal = { background = texture, textColor = Parse("#F5F7FF") },
                    border = new RectOffset(3, 3, 3, 3),
                    padding = padding,
                    margin = margin
                };
            }

            private static GUIStyle CreateChipStyle(string fillHex, string borderHex, string textHex, int fontSize, FontStyle fontStyle, RectOffset padding)
            {
                return new GUIStyle(EditorStyles.miniLabel)
                {
                    fontSize = fontSize,
                    fontStyle = fontStyle,
                    alignment = TextAnchor.MiddleCenter,
                    padding = padding,
                    margin = new RectOffset(0, 0, 0, 0),
                    normal =
                    {
                        background = CreateBorderedTexture(Parse(fillHex), Parse(borderHex)),
                        textColor = Parse(textHex)
                    },
                    border = new RectOffset(3, 3, 3, 3)
                };
            }

            private static GUIStyle CreateButtonStyle(string fillHex, string hoverHex, string textHex, int fontSize, FontStyle fontStyle)
            {
                return new GUIStyle(GUI.skin.button)
                {
                    fontSize = fontSize,
                    fontStyle = fontStyle,
                    alignment = TextAnchor.MiddleCenter,
                    border = new RectOffset(3, 3, 3, 3),
                    padding = new RectOffset(14, 14, 8, 8),
                    margin = new RectOffset(0, 0, 0, 0),
                    normal =
                    {
                        background = CreateBorderedTexture(Parse(fillHex), Shift(Parse(fillHex), 0.18f)),
                        textColor = Parse(textHex)
                    },
                    hover =
                    {
                        background = CreateBorderedTexture(Parse(hoverHex), Shift(Parse(hoverHex), 0.12f)),
                        textColor = Parse("#FFFFFF")
                    },
                    active =
                    {
                        background = CreateBorderedTexture(Shift(Parse(fillHex), -0.08f), Shift(Parse(fillHex), 0.08f)),
                        textColor = Parse(textHex)
                    }
                };
            }

            private static GUIStyle CreateInputStyle(string fillHex, string borderHex, string textHex)
            {
                return new GUIStyle(EditorStyles.textField)
                {
                    fontSize = 12,
                    border = new RectOffset(3, 3, 3, 3),
                    padding = new RectOffset(10, 10, 6, 6),
                    margin = new RectOffset(0, 0, 0, 0),
                    normal =
                    {
                        background = CreateBorderedTexture(Parse(fillHex), Parse(borderHex)),
                        textColor = Parse(textHex)
                    },
                    focused =
                    {
                        background = CreateBorderedTexture(Parse(fillHex), Shift(Parse(borderHex), 0.12f)),
                        textColor = Parse(textHex)
                    }
                };
            }

            private static GUIStyle CreateTextAreaStyle(string fillHex, string borderHex, string textHex)
            {
                var style = CreateInputStyle(fillHex, borderHex, textHex);
                style.wordWrap = true;
                style.alignment = TextAnchor.UpperLeft;
                style.stretchHeight = true;
                return style;
            }

            private static GUIStyle CreateLabelStyle(int fontSize, FontStyle fontStyle, string textHex)
            {
                return new GUIStyle(EditorStyles.label)
                {
                    fontSize = fontSize,
                    fontStyle = fontStyle,
                    normal = { textColor = Parse(textHex) }
                };
            }

            private static GUIStyle CreateWrappedLabelStyle(int fontSize, FontStyle fontStyle, string textHex)
            {
                return new GUIStyle(EditorStyles.label)
                {
                    fontSize = fontSize,
                    fontStyle = fontStyle,
                    wordWrap = true,
                    normal = { textColor = Parse(textHex) }
                };
            }

            private static Texture2D CreateBorderedTexture(Color fill, Color border)
            {
                var tex = new Texture2D(8, 8);
                var pixels = new Color[64];
                for (int y = 0; y < 8; y++)
                {
                    for (int x = 0; x < 8; x++)
                    {
                        var index = y * 8 + x;
                        var isBorder = x <= 1 || y <= 1 || x >= 6 || y >= 6;
                        pixels[index] = isBorder ? border : fill;
                    }
                }

                tex.SetPixels(pixels);
                tex.Apply();
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.hideFlags = HideFlags.HideAndDontSave;
                return tex;
            }

            private static Color Parse(string hex)
            {
                ColorUtility.TryParseHtmlString(hex, out var color);
                return color;
            }

            private static Color Shift(Color color, float delta)
            {
                return new Color(
                    Mathf.Clamp01(color.r + delta),
                    Mathf.Clamp01(color.g + delta),
                    Mathf.Clamp01(color.b + delta),
                    color.a);
            }
        }
    }
}
#endif
