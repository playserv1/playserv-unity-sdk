#if UNITY_EDITOR
using System.Collections.Generic;
using System.Threading;
using UnityEditor;
using UnityEngine;
using Playserv.Editor.Proxy;
using Playserv.Wrapper;

namespace Playserv.Editor
{
    public sealed partial class PlayServWindow : EditorWindow
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
        private List<string> _deployFilesPreview = new List<string>();

        private bool _deployRunning;
        private float _deployProgress;
        private string _deployStatus = string.Empty;
        private CancellationTokenSource _deployCts;
        private bool _versionSyncRunning;
        private string _versionSyncStatus = string.Empty;

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

            var wnd = GetWindow<PlayServWindow>(title: WindowTitlePrefix);
            wnd.titleContent = new GUIContent(BuildWindowTitle(PlayServConfigProvider.FindExisting()?.SdkVersion));
            wnd.minSize = new Vector2(MinWindowWidth, 760f);
            if (wnd.position.width > DefaultWindowWidth)
                wnd.position = new Rect(wnd.position.x, wnd.position.y, DefaultWindowWidth, Mathf.Max(wnd.position.height, 760f));
            wnd.Show();
            wnd.Focus();
        }
    }
}
#endif
