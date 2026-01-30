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
        [SerializeField] private string gameAccessToken;
        [SerializeField] private string gameVersion = "1.0.0";
        [SerializeField] private string sdkVersion = SdkInfo.Version;
        [SerializeField] private bool allowMultipleConnections = true;
        [SerializeField] private int keepAlivePingIntervalMs = 30000;
        [SerializeField] private int keepAlivePongTimeoutMs = 10000;

        public string GameAccessToken => gameAccessToken;
        public string GameVersion => gameVersion;
        public string SdkVersion => sdkVersion;
        public bool AllowMultipleConnections => allowMultipleConnections;
        public int KeepAlivePingIntervalMs => keepAlivePingIntervalMs;
        public int KeepAlivePongTimeoutMs => keepAlivePongTimeoutMs;

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