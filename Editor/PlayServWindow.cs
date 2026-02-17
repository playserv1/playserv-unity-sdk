#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Playserv.Wrapper;
using Playserv.CodeGenerator.Editor;
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
        private const string LegacyLocalDeployEndpoint = "http://localhost:5000/api/deployments";
        private const string DefaultBackofficeDeployEndpoint = "https://playserv-backoffice.test.playserv.io/api/deployments";

        private PlayServConfig _config;
        private SerializedObject _so;

        private SerializedProperty _pGameAccessToken;
        private SerializedProperty _pUserId;
        private SerializedProperty _pGameId;
        private SerializedProperty _pProjectId;
        private SerializedProperty _pGameVersion;
        private SerializedProperty _pSdkVersion;
        private SerializedProperty _pAllowMultipleConnections;
        private SerializedProperty _pDeployApiEndpoint;
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

        [MenuItem(MenuPath)]
        public static void ShowFromMenu() => ShowWindow();

        [InitializeOnLoadMethod]
        private static void OnEditorLoad()
        {
            if (!EditorPrefs.HasKey(Const.PrefKeyShowOnStartup))
                EditorPrefs.SetBool(Const.PrefKeyShowOnStartup, true);

            if (!EditorPrefs.HasKey(Const.PrefKeyAutoCodegen))
                EditorPrefs.SetBool(Const.PrefKeyAutoCodegen, true);

            if (EditorPrefs.GetBool(Const.PrefKeyShowOnStartup, true))
                EditorApplication.delayCall += ShowWindow;
        }

        private static void ShowWindow()
        {
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
                "wss://playserv-proxy.test.playserv.io/ws"
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
            _pUserId = _so.FindProperty("userId");
            _pGameId = _so.FindProperty("gameId");
            _pProjectId = _so.FindProperty("projectId");
            _pGameVersion = _so.FindProperty("gameVersion");
            _pSdkVersion = _so.FindProperty("sdkVersion");
            _pAllowMultipleConnections = _so.FindProperty("allowMultipleConnections");
            _pDeployApiEndpoint = _so.FindProperty("deployApiEndpoint");
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
            DrawConnectionFoldout();
            GUILayout.Space(6);
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
                    SchemaLoader.LoadSchema(_pGameId.stringValue);
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

                    if (_pDeployApiEndpoint != null)
                        EditorGUILayout.PropertyField(_pDeployApiEndpoint, new GUIContent("Deploy Endpoint"));

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

                using (new EditorGUI.DisabledScope(_deployRunning))
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
                .Where(f => !f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (list.Count == 0)
                error = "No files matched the current pattern.";

            return list;
        }

        private async Task StartDeployAsync()
        {
            if (_deployRunning)
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
                    await DeployWithRelativePathsAsync(api, gameId, files, _deployFolder, _deployCts.Token);
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

        private string ResolveDeployEndpointForDisplay()
        {
            var endpoint = _config?.DeployApiEndpoint?.Trim();
            if (string.IsNullOrWhiteSpace(endpoint))
                return DefaultBackofficeDeployEndpoint;

            if (string.Equals(endpoint, LegacyLocalDeployEndpoint, StringComparison.OrdinalIgnoreCase))
                return DefaultBackofficeDeployEndpoint;

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
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.ObjectField("Config Asset", _config, typeof(PlayServConfig), false);
                    if (GUILayout.Button("Ping", GUILayout.Width(60)))
                        EditorGUIUtility.PingObject(_config);
                    EditorGUILayout.EndHorizontal();

                    GUILayout.Space(6);

                    _so.Update();

                    EditorGUILayout.PropertyField(_pGameAccessToken);
                    EditorGUILayout.PropertyField(_pProjectId);
                    EditorGUILayout.PropertyField(_pUserId);
                    EditorGUILayout.PropertyField(_pGameId);
                    EditorGUILayout.PropertyField(_pGameVersion);
                    EditorGUILayout.PropertyField(_pAllowMultipleConnections);
                    
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
