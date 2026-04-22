#if UNITY_EDITOR
using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Playserv.CodeGenerator.Editor;
using Playserv.Editor.Proxy;
using Playserv.Events.Editor;
using Playserv.ModelGenerator.Editor;
using Playserv.Wrapper;

namespace Playserv.Editor
{
    public sealed partial class PlayServWindow
    {
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
    }
}
#endif
