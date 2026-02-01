using Playserv.Proxy.Common;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Playserv.Wrapper
{
    [CreateAssetMenu(fileName = "PlayServConfig", menuName = "PlayServ/Config", order = 0)]
    public sealed class PlayServConfig : ScriptableObject
    {
        private const string DEFAULT_LOCAL_ENDPOINT = "ws://localhost:8080/ws/";
        private const string DEFAULT_REMOTE_ENDPOINT = "wss://playserv-proxy.test.playserv.io/ws";

        [SerializeField] private string gameAccessToken;
        [SerializeField] private string gameId;
        [SerializeField] private string userId;
        [SerializeField] private string gameVersion = "1.0.0";
        [SerializeField] private string sdkVersion = SdkInfo.Version;
        [SerializeField] private bool allowMultipleConnections = true;
        [SerializeField] private int keepAlivePingIntervalMs = 30000;
        [SerializeField] private int keepAlivePongTimeoutMs = 10000;
        [SerializeField] private int networkTransformSyncIntervalMs = 100;
        [SerializeField] private bool useLocalBackend = false;
        [SerializeField] private string localEndpoint = DEFAULT_LOCAL_ENDPOINT;
        [SerializeField] private string remoteEndpoint = DEFAULT_REMOTE_ENDPOINT;

        public string GameAccessToken => gameAccessToken;

        public string GameId => gameId;

        public string UserId
        {
            get => userId;
            set => userId = value;
        }

        public string GameVersion => gameVersion;

        public string SdkVersion => sdkVersion;

        public bool AllowMultipleConnections => allowMultipleConnections;
        public int KeepAlivePingIntervalMs => keepAlivePingIntervalMs;
        public int KeepAlivePongTimeoutMs => keepAlivePongTimeoutMs;

        public int NetworkTransformSyncIntervalMs => networkTransformSyncIntervalMs;

        public bool UseLocalBackend => useLocalBackend;
        public string LocalEndpoint => localEndpoint;
        public string RemoteEndpoint => remoteEndpoint;
        public string Endpoint => useLocalBackend ? localEndpoint : remoteEndpoint;

        public void SetAllowMultipleConnections(bool value)
        {
            allowMultipleConnections = value;
#if UNITY_EDITOR
            EditorUtility.SetDirty(this);
#endif
        }

        public void SetGameAccessToken(string value)
        {
            gameAccessToken = value;
#if UNITY_EDITOR
            EditorUtility.SetDirty(this);
#endif
        }

        public void SetGameVersion(string value)
        {
            gameVersion = value;
#if UNITY_EDITOR
            EditorUtility.SetDirty(this);
#endif
        }

        public void SetSdkVersion(string value)
        {
            sdkVersion = value;
#if UNITY_EDITOR
            EditorUtility.SetDirty(this);
#endif
        }
    }
}
