#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using Playserv.Wrapper;
using Playserv.CodeGenerator.Editor;
using Playserv.Events.Editor;
using Playserv.ModelGenerator.Editor;

namespace Playserv.Editor
{
    public sealed class PlayServWindow : EditorWindow
    {
        private const string MenuPath = "Tools/PlayServ/Settings";
        private const string DocsUrl = "https://example.com";

        private PlayServConfig _config;
        private SerializedObject _so;

        private SerializedProperty _pGameAccessToken;
        private SerializedProperty _pUserId;
        private SerializedProperty _pGameId;
        private SerializedProperty _pGameVersion;
        private SerializedProperty _pSdkVersion;
        private SerializedProperty _pAllowMultipleConnections;

        private bool _foldCodegen;
        private bool _foldEvents;
        private bool _foldModel;
        private bool _foldConfig;

        private bool _showAvailableSchemaInfo;

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
            wnd.minSize = new Vector2(520, 360);
            wnd.Show();
            wnd.Focus();
        }

        private void OnEnable()
        {
            _foldCodegen = EditorPrefs.GetBool(Const.PrefFoldCodegen, true);
            _foldConfig = EditorPrefs.GetBool(Const.PrefFoldConfig, true);

            _showAvailableSchemaInfo = false;

            EnsureConfig();
        }

        private void EnsureConfig()
        {
            _config = PlayServConfigProvider.GetOrCreate();
            _so = new SerializedObject(_config);

            _pGameAccessToken = _so.FindProperty("gameAccessToken");
            _pUserId = _so.FindProperty("userId");
            _pGameId = _so.FindProperty("gameId");
            _pGameVersion = _so.FindProperty("gameVersion");
            _pSdkVersion = _so.FindProperty("sdkVersion");
            _pAllowMultipleConnections = _so.FindProperty("allowMultipleConnections");
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
            DrawConfigFoldout();

            GUILayout.FlexibleSpace();
            DrawFooter();
            GUILayout.Space(6);
        }

        private void DrawCodegenFoldout()
        {
            _foldCodegen = EditorGUILayout.BeginFoldoutHeaderGroup(
                _foldCodegen,
                "Code Generation");

            if (_foldCodegen)
            {
                EditorGUI.indentLevel++;

                bool autoGen = EditorPrefs.GetBool(Const.PrefKeyAutoCodegen, true);
                bool newAutoGen = EditorGUILayout.ToggleLeft(
                    "Enable automatic DTO generation",
                    autoGen);

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

        private void DrawConfigFoldout()
        {
            _foldConfig = EditorGUILayout.BeginFoldoutHeaderGroup(
                _foldConfig,
                "PlayServ Config");

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
                    EditorGUILayout.PropertyField(_pUserId);
                    EditorGUILayout.PropertyField(_pGameId);
                    EditorGUILayout.PropertyField(_pGameVersion);
                    EditorGUILayout.PropertyField(_pAllowMultipleConnections);

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("SDK Version", EditorStyles.miniBoldLabel);
                    EditorGUILayout.SelectableLabel(
                        _pSdkVersion.stringValue,
                        EditorStyles.textField,
                        GUILayout.Height(EditorGUIUtility.singleLineHeight));
                    EditorGUILayout.EndHorizontal();
                    
                    
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
            bool newShowOnStartup = EditorGUILayout.ToggleLeft(
                "Show this window on Unity startup",
                showOnStartup);

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