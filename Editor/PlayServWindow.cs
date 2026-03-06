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
        private const string MenuPath = "Tools/PlayServ/Settings";
        private const string DocsUrl = "https://example.com";
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

        private EditorWebSocketTransport _wsTransport;
        private Vector2 _connectionScrollPos;
        private string _wsEndpoint = "ws://localhost:8080";
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

            var wnd = GetWindow<PlayServWindow>(utility: true, title: "PlayServ");
            wnd.minSize = new Vector2(520, 520);
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

            _wsEndpoint = EditorPrefs.GetString(
                Const.PrefKeyWebSocketEndpoint,
                PlayServSettings.DefaultBackendServerAddress
            );

            EnsureConfig();
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
        }

        private void OnGUI()
        {
            GUILayout.Space(8);

            EditorGUILayout.HelpBox(
                "Central settings for PlayServ SDK and code generation.",
                MessageType.Info);

            GUILayout.Space(6);

            DrawCodegenFoldout();
            GUILayout.Space(6);
            DrawEventsFoldout();
            GUILayout.Space(6);
            DrawModelFoldout();
            GUILayout.Space(6);
            if (ShowWebSocketConnectionMenu)
            {
                DrawConnectionFoldout();
                GUILayout.Space(6);
            }
            DrawDeploymentFoldout();
            GUILayout.Space(6);

            DrawConfigFoldout();

            GUILayout.FlexibleSpace();
            DrawFooter();
            GUILayout.Space(6);
            
            if (_deployRunning)
                Repaint();
        }

        private void DrawCodegenFoldout()
        {
            _foldCodegen = EditorGUILayout.BeginFoldoutHeaderGroup(_foldCodegen, "Code Generation");

            if (_foldCodegen)
            {
                EditorGUI.indentLevel++;

                bool autoGen = EditorPrefs.GetBool(Const.PrefKeyAutoCodegen, true);
                bool newAutoGen = EditorGUILayout.ToggleLeft("Enable automatic DTO generation", autoGen);

                if (newAutoGen != autoGen)
                    EditorPrefs.SetBool(Const.PrefKeyAutoCodegen, newAutoGen);

                GUILayout.Space(6);

                if (GUILayout.Button("Generate DTOs Now"))
                    SharedCodeGenerator.GenerateMenu();

                if (GUILayout.Button("Remove Generated DTOs"))
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

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
            EditorPrefs.SetBool(Const.PrefFoldCodegen, _foldCodegen);
        }

        private void DrawEventsFoldout()
        {
            _foldEvents = EditorGUILayout.BeginFoldoutHeaderGroup(_foldEvents, "Events");

            if (_foldEvents)
            {
                EditorGUI.indentLevel++;

                if (GUILayout.Button("Generate Events API"))
                    EventsCodeGenerator.Generate();

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
            EditorPrefs.SetBool(Const.PrefFoldEvents, _foldEvents);
        }

        private void DrawModelFoldout()
        {
            _foldModel = EditorGUILayout.BeginFoldoutHeaderGroup(_foldModel, "Model");

            if (_foldModel)
            {
                EditorGUI.indentLevel++;

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
                        EditorGUILayout.HelpBox(statusMessage, statusType.Value);

                    EditorGUILayout.LabelField("Schema details", EditorStyles.boldLabel);

                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(" ", GUILayout.Width(14));
                    EditorGUILayout.LabelField("Current schema", EditorStyles.miniBoldLabel);
                    if (_showAvailableSchemaInfo)
                        EditorGUILayout.LabelField("Latest available schema", EditorStyles.miniBoldLabel);
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("V", GUILayout.Width(14));
                    EditorGUILayout.SelectableLabel(
                        string.IsNullOrEmpty(currentVersion) ? "—" : currentVersion,
                        EditorStyles.textField,
                        GUILayout.Height(EditorGUIUtility.singleLineHeight));

                    if (_showAvailableSchemaInfo)
                    {
                        EditorGUILayout.SelectableLabel(
                            string.IsNullOrEmpty(latestVersion) ? "—" : latestVersion,
                            EditorStyles.textField,
                            GUILayout.Height(EditorGUIUtility.singleLineHeight));
                    }

                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("T", GUILayout.Width(14));
                    EditorGUILayout.SelectableLabel(
                        string.IsNullOrEmpty(currentTimestampRaw) ? "—" : FormatTimestamp(currentTimestampRaw),
                        EditorStyles.textField,
                        GUILayout.Height(EditorGUIUtility.singleLineHeight));

                    if (_showAvailableSchemaInfo)
                    {
                        EditorGUILayout.SelectableLabel(
                            string.IsNullOrEmpty(latestTimestampRaw) ? "—" : FormatTimestamp(latestTimestampRaw),
                            EditorStyles.textField,
                            GUILayout.Height(EditorGUIUtility.singleLineHeight));
                    }

                    EditorGUILayout.EndHorizontal();

                    if (_showAvailableSchemaInfo)
                    {
                        GUILayout.Space(6);

                        EditorGUILayout.BeginHorizontal();

                        if (GUILayout.Button("Hide", GUILayout.Width(120)))
                            _showAvailableSchemaInfo = false;

                        GUILayout.FlexibleSpace();

                        using (new EditorGUI.DisabledScope(!differs))
                        {
                            if (GUILayout.Button("Apply New Schema", GUILayout.Width(180)))
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

                if (GUILayout.Button("Check Updates"))
                {
                    _showAvailableSchemaInfo = true;
                    SchemaLoader.LoadSchema(_pGameAccessToken.stringValue);
                    SchemaLoader.CheckNewSchema();
                }

                if (GUILayout.Button("Re-generate Models from current schema"))
                    SchemaCodeGenerator.GenerateModels(false);

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
            EditorPrefs.SetBool(Const.PrefFoldModel, _foldModel);
        }

        private void DrawConnectionFoldout()
        {
            _foldConnection = EditorGUILayout.BeginFoldoutHeaderGroup(_foldConnection, "WebSocket Connection");

            if (_foldConnection)
            {
                EditorGUI.indentLevel++;

                EditorGUILayout.HelpBox("Test WebSocket connection.", MessageType.Info);

                GUILayout.Space(6);

                EditorGUILayout.LabelField("Connection Settings", EditorStyles.boldLabel);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Endpoint", GUILayout.Width(80));
                string newEndpoint = EditorGUILayout.TextField(_wsEndpoint);
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
                    if (GUILayout.Button(isConnecting ? "Connecting..." : "Connect", GUILayout.Height(30)))
                        _ = ConnectWebSocket();
                }

                using (new EditorGUI.DisabledScope(!isConnected))
                {
                    if (GUILayout.Button("Disconnect", GUILayout.Height(30)))
                        DisconnectWebSocket();
                }

                EditorGUILayout.EndHorizontal();

                GUILayout.Space(6);

                EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);
                string statusText = isConnected ? "Connected" : isConnecting ? "Connecting..." : "Disconnected";
                var statusColor = isConnected ? Color.green : isConnecting ? Color.yellow : Color.gray;

                var prevColor = GUI.color;
                GUI.color = statusColor;
                EditorGUILayout.LabelField("● " + statusText, EditorStyles.boldLabel);
                GUI.color = prevColor;

                GUILayout.Space(6);

                using (new EditorGUI.DisabledScope(!isConnected))
                {
                    EditorGUILayout.LabelField("Send Test Message", EditorStyles.boldLabel);
                    _testMessage = EditorGUILayout.TextArea(_testMessage, GUILayout.Height(40));

                    if (GUILayout.Button("Send Message"))
                        _ = SendTestMessage();
                }

                GUILayout.Space(6);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Connection Log", EditorStyles.boldLabel);
                if (GUILayout.Button("Clear", GUILayout.Width(60)))
                {
                    _wsTransport?.ClearLogs();
                    Repaint();
                }
                EditorGUILayout.EndHorizontal();

                _connectionScrollPos = EditorGUILayout.BeginScrollView(_connectionScrollPos, GUILayout.Height(200));

                if (_wsTransport != null && _wsTransport.LogMessages.Count > 0)
                {
                    foreach (var log in _wsTransport.LogMessages)
                        EditorGUILayout.SelectableLabel(log, EditorStyles.wordWrappedLabel, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                }
                else
                {
                    EditorGUILayout.LabelField("No logs yet...", EditorStyles.centeredGreyMiniLabel);
                }

                EditorGUILayout.EndScrollView();

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
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
            _foldDeployment = EditorGUILayout.BeginFoldoutHeaderGroup(_foldDeployment, "Deployment");

            if (_foldDeployment)
            {
                EditorGUI.indentLevel++;

                EditorGUILayout.HelpBox(
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
                    _deployPattern = EditorGUILayout.TextField(_deployPattern);
                }

                _deployKeepRelativePaths = EditorGUILayout.ToggleLeft(
                    "Keep relative paths in ZIP (recommended)",
                    _deployKeepRelativePaths);

                GUILayout.Space(6);

                using (new EditorGUI.DisabledScope(_deployRunning || _versionSyncRunning))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Preview Files", GUILayout.Width(120)))
                        {
                            _deployFilesPreview = BuildDeployFileList(out var err);
                            if (!string.IsNullOrEmpty(err))
                                Debug.LogError($"[PlayServ] {err}");
                            else
                                _deployShowFileList = true;
                        }

                        if (GUILayout.Button("Clear Preview", GUILayout.Width(120)))
                        {
                            _deployFilesPreview.Clear();
                            _deployShowFileList = false;
                        }

                        GUILayout.FlexibleSpace();

                        if (GUILayout.Button("Sync Version", GUILayout.Width(120)))
                            _ = StartVersionSyncAsync();

                        if (GUILayout.Button("Deploy Now", GUILayout.Width(140)))
                            _ = StartDeployAsync();
                    }
                }

                using (new EditorGUI.DisabledScope(!_deployRunning))
                {
                    if (GUILayout.Button("Cancel", GUILayout.Width(120)))
                        _deployCts?.Cancel();
                }

                if (_deployShowFileList && _deployFilesPreview.Count > 0)
                {
                    GUILayout.Space(6);
                    EditorGUILayout.LabelField($"Files ({_deployFilesPreview.Count})", EditorStyles.miniBoldLabel);

                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        _deployFilesScroll = EditorGUILayout.BeginScrollView(_deployFilesScroll, GUILayout.Height(140));
                        foreach (var f in _deployFilesPreview.Take(300))
                            EditorGUILayout.LabelField(f, EditorStyles.miniLabel);
                        if (_deployFilesPreview.Count > 300)
                            EditorGUILayout.LabelField($"...and {_deployFilesPreview.Count - 300} more", EditorStyles.miniLabel);
                        EditorGUILayout.EndScrollView();
                    }
                }

                if (_deployRunning)
                {
                    GUILayout.Space(6);
                    EditorGUILayout.LabelField("Status", EditorStyles.miniBoldLabel);
                    EditorGUILayout.HelpBox(string.IsNullOrEmpty(_deployStatus) ? "Working..." : _deployStatus, MessageType.None);
                    EditorGUILayout.Slider("Progress", _deployProgress, 0f, 1f);
                }

                if (_versionSyncRunning || !string.IsNullOrWhiteSpace(_versionSyncStatus))
                {
                    GUILayout.Space(6);
                    EditorGUILayout.LabelField("Version Sync", EditorStyles.miniBoldLabel);
                    EditorGUILayout.HelpBox(_versionSyncStatus, _versionSyncRunning ? MessageType.Info : MessageType.None);
                }

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
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
            if (string.IsNullOrWhiteSpace(endpoint))
                return PlayServSettings.DefaultDeployApiServerAddress;

            return endpoint;
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
            _foldConfig = EditorGUILayout.BeginFoldoutHeaderGroup(_foldConfig, "PlayServ Config");

            if (_foldConfig)
            {
                EditorGUI.indentLevel++;

                if (_config == null || _so == null)
                {
                    if (GUILayout.Button("Create / Locate Config"))
                        EnsureConfig();

                    EditorGUILayout.HelpBox("Config asset not found.", MessageType.Warning);
                }
                else
                {
                    DrawEnvironmentSummary();
                    GUILayout.Space(6);

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.ObjectField("Config Asset", _config, typeof(PlayServConfig), false);
                    if (GUILayout.Button("Ping", GUILayout.Width(60)))
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
                            EditorStyles.textField,
                            GUILayout.Height(EditorGUIUtility.singleLineHeight));
                    }

                    if (_so.ApplyModifiedProperties())
                        EditorUtility.SetDirty(_config);
                }

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
            EditorPrefs.SetBool(Const.PrefFoldConfig, _foldConfig);
        }

        private void DrawEnvironmentSummary()
        {
            var environments = PlayServEnvironmentResolver.Environments;
            if (environments.Length == 0)
                return;

            var activeEnvironment = ResolveActiveEnvironmentNameForDisplay();
            var activeIndex = Array.IndexOf(environments, activeEnvironment);
            if (activeIndex < 0)
                activeIndex = 0;

            var labels = environments
                .Select(AsDisplayEnvironmentLabel)
                .ToArray();

            var canSwitchInClientEditor = CanSwitchEnvironmentInClientEditor();
            int selectedIndex;
            using (new EditorGUI.DisabledScope(!canSwitchInClientEditor))
            {
                selectedIndex = EditorGUILayout.Popup("Environment", activeIndex, labels);
            }

            if (canSwitchInClientEditor && selectedIndex != activeIndex)
            {
                var selectedEnvironment = environments[selectedIndex];
                if (TrySetEnvironmentInClientEditor(selectedEnvironment, out var error))
                {
                    EnsureConfig();
                    Repaint();
                }
                else
                {
                    Debug.LogError($"[PlayServ] Failed to switch environment: {error}");
                }
            }
        }

        private static string ResolveActiveEnvironmentNameForDisplay()
        {
            if (PlayServEnvironmentResolver.TryLoadConfigFromFile(out var config, out _))
                return PlayServEnvironmentResolver.ResolveEnvironmentName(config.ActiveEnvironment);

            return PlayServEnvironmentResolver.ResolveEnvironmentName(null);
        }

        private static bool CanSwitchEnvironmentInClientEditor()
        {
            return TryGetClientEnvironmentSetMethod(out _);
        }

        private static bool TrySetEnvironmentInClientEditor(string environmentName, out string error)
        {
            if (!TryGetClientEnvironmentSetMethod(out var setMethod))
            {
                error = "Client environment manager method TrySetActiveEnvironment is not available.";
                return false;
            }

            var args = new object[] { environmentName, string.Empty };
            var invocationResult = setMethod.Invoke(null, args);
            var success = invocationResult is bool b && b;
            error = args[1] as string ?? string.Empty;
            return success;
        }

        private static bool TryGetClientEnvironmentSetMethod(out MethodInfo setMethod)
        {
            setMethod = null;

            var managerType = FindClientEnvironmentManagerType();
            if (managerType == null)
                return false;

            setMethod = managerType.GetMethod(
                "TrySetActiveEnvironment",
                BindingFlags.Static | BindingFlags.NonPublic);

            return setMethod != null;
        }

        private static Type FindClientEnvironmentManagerType()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType("Playserv.ClientEditor.PlayServEnvironmentManager", throwOnError: false);
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
                    EditorStyles.textField,
                    GUILayout.Height(EditorGUIUtility.singleLineHeight));
            }
        }

        private static string AsDisplayEnvironmentLabel(string environmentName)
        {
            if (string.IsNullOrWhiteSpace(environmentName))
                return "Unknown";

            var normalized = environmentName.Trim().ToLowerInvariant();
            return normalized switch
            {
                "local" => "Local",
                "dev" => "Dev",
                "test" => "Test",
                "prod" => "Prod",
                _ => environmentName
            };
        }

        private void DrawFooter()
        {
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

            EditorGUILayout.BeginHorizontal();

            bool showOnStartup = EditorPrefs.GetBool(Const.PrefKeyShowOnStartup, true);
            bool newShowOnStartup = EditorGUILayout.ToggleLeft("Show this window on Unity startup", showOnStartup);

            if (newShowOnStartup != showOnStartup)
                EditorPrefs.SetBool(Const.PrefKeyShowOnStartup, newShowOnStartup);

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Open Docs", GUILayout.Width(100)))
                Application.OpenURL(DocsUrl);

            EditorGUILayout.EndHorizontal();
        }
    }
}
#endif
