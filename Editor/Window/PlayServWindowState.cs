#if UNITY_EDITOR
using System.Collections.Generic;
using System.Threading;
using UnityEditor;
using UnityEngine;
using Playserv.Editor.Proxy;
using Playserv.Wrapper;

namespace Playserv.Editor
{
    internal sealed class PlayServWindowState
    {
        public PlayServConfig Config;
        public SerializedObject SerializedObject;

        public SerializedProperty GameAccessTokenProperty;
        public SerializedProperty GameIdProperty;
        public SerializedProperty GameVersionProperty;
        public SerializedProperty SdkVersionProperty;
        public SerializedProperty AllowMultipleConnectionsProperty;
        public SerializedProperty DeployAuthTokenProperty;
        public SerializedProperty DeployTimeoutSecondsProperty;

        public bool FoldCodegen;
        public bool FoldEvents;
        public bool FoldModel;
        public bool FoldConfig;
        public bool FoldConnection;
        public bool FoldDeployment;
        public bool ShowModuleSettingsLayer;

        public bool ShowAvailableSchemaInfo;
        public Vector2 MainScrollPos;
        public readonly PlayServEditorModuleSettings ModuleSettings = new PlayServEditorModuleSettings();

        public EditorWebSocketTransport WebSocketTransport;
        public Vector2 ConnectionScrollPos;
        public string WebSocketEndpoint = string.Empty;
        public string TestMessage = "{\"type\":\"ping\"}";

        public DefaultAsset DeployFolder;
        public bool DeployIncludeSubfolders = true;
        public string DeployPattern = "*";
        public bool DeployKeepRelativePaths = true;
        public bool DeployShowFileList;
        public Vector2 DeployFilesScroll;
        public List<string> DeployFilesPreview = new List<string>();

        public bool DeployRunning;
        public float DeployProgress;
        public string DeployStatus = string.Empty;
        public CancellationTokenSource DeployCts;
        public bool VersionSyncRunning;
        public string VersionSyncStatus = string.Empty;
    }
}
#endif
