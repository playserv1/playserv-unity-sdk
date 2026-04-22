#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Playserv.Wrapper;

namespace Playserv.Editor
{
    public sealed partial class PlayServWindow
    {
        private void OnEnable()
        {
            _foldCodegen = EditorPrefs.GetBool(Const.PrefFoldCodegen, true);
            _foldConfig = EditorPrefs.GetBool(Const.PrefFoldConfig, true);
            _foldConnection = EditorPrefs.GetBool(Const.PrefFoldConnection, false);
            _foldDeployment = EditorPrefs.GetBool(Const.PrefFoldDeployment, false);

            _showAvailableSchemaInfo = false;

            if (position.width > DefaultWindowWidth)
                position = new Rect(position.x, position.y, DefaultWindowWidth, Mathf.Max(position.height, 760f));

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
    }
}
#endif
