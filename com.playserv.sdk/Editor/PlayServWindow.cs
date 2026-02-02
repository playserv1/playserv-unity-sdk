#if UNITY_EDITOR
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
        private const string PrefKeyShowOnStartup = "PlayServ.Window.ShowOnStartup";
        private const string PrefKeyAutoCodegen   = "PlayServ.Codegen.AutoGenerate";

        private const string PrefFoldCodegen = "PlayServ.Window.Fold.Codegen";
        private const string PrefFoldEvents = "PlayServ.Window.Fold.Events";
        private const string PrefFoldModel = "PlayServ.Window.Fold.Model";
        private const string PrefFoldConfig  = "PlayServ.Window.Fold.Config";

        private const string MenuPath = "Tools/PlayServ/Settings";
        private const string DocsUrl  = "https://example.com";

        private PlayServConfig _config;
        private SerializedObject _so;

        private SerializedProperty _pGameAccessToken;
        private SerializedProperty _pGameVersion;
        private SerializedProperty _pSdkVersion;
        private SerializedProperty _pAllowMultipleConnections;

        private bool _foldCodegen;
        private bool _foldEvents;
        private bool _foldModel;
        private bool _foldConfig;

        [MenuItem(MenuPath)]
        public static void ShowFromMenu() => ShowWindow();

        [InitializeOnLoadMethod]
        private static void OnEditorLoad()
        {
            if (!EditorPrefs.HasKey(PrefKeyShowOnStartup))
                EditorPrefs.SetBool(PrefKeyShowOnStartup, true);

            if (!EditorPrefs.HasKey(PrefKeyAutoCodegen))
                EditorPrefs.SetBool(PrefKeyAutoCodegen, true);

            if (EditorPrefs.GetBool(PrefKeyShowOnStartup, true))
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
            _foldCodegen = EditorPrefs.GetBool(PrefFoldCodegen, true);
            _foldConfig  = EditorPrefs.GetBool(PrefFoldConfig, true);
            EnsureConfig();
        }

        private void EnsureConfig()
        {
            _config = PlayServConfigProvider.GetOrCreate();
            _so = new SerializedObject(_config);

            _pGameAccessToken = _so.FindProperty("gameAccessToken");
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

                bool autoGen = EditorPrefs.GetBool(PrefKeyAutoCodegen, true);
                bool newAutoGen = EditorGUILayout.ToggleLeft(
                    "Enable automatic DTO generation",
                    autoGen);

                if (newAutoGen != autoGen)
                    EditorPrefs.SetBool(PrefKeyAutoCodegen, newAutoGen);

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
            EditorPrefs.SetBool(PrefFoldCodegen, _foldCodegen);
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
            EditorPrefs.SetBool(PrefFoldEvents, _foldEvents);
        }

        private void DrawModelFoldout()
        {
            _foldModel = EditorGUILayout.BeginFoldoutHeaderGroup(_foldModel, "Model");

            if (_foldModel)
            {
                EditorGUI.indentLevel++;
                
                if (GUILayout.Button("Load JSON Schema"))
                    SchemaLoader.LoadSchema();
                
                if (GUILayout.Button("Generate Models from JSON Schema"))
                    SchemaCodeGenerator.GenerateModels();
                
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
            EditorPrefs.SetBool(PrefFoldModel, _foldModel);
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
                    EditorGUILayout.PropertyField(_pGameVersion);
                    EditorGUILayout.PropertyField(_pSdkVersion);
                    EditorGUILayout.PropertyField(_pAllowMultipleConnections);

                    if (_so.ApplyModifiedProperties())
                        EditorUtility.SetDirty(_config);
                }

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
            EditorPrefs.SetBool(PrefFoldConfig, _foldConfig);
        }

        private void DrawFooter()
        {
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

            EditorGUILayout.BeginHorizontal();

            bool showOnStartup = EditorPrefs.GetBool(PrefKeyShowOnStartup, true);
            bool newShowOnStartup = EditorGUILayout.ToggleLeft(
                "Show this window on Unity startup",
                showOnStartup);

            if (newShowOnStartup != showOnStartup)
                EditorPrefs.SetBool(PrefKeyShowOnStartup, newShowOnStartup);

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Open Docs", GUILayout.Width(100)))
                Application.OpenURL(DocsUrl);

            EditorGUILayout.EndHorizontal();
        }
    }
}
#endif