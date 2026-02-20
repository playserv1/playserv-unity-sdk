using System;
using System.IO;
using System.Threading.Tasks;
using Playserv.Proxy.Common;
using Playserv.Wrapper;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace Playserv.Samples
{
    /// <summary>
    /// Minimal bootstrap component for configuring and connecting PlayServ from a scene.
    /// </summary>
    public sealed class PlayServBootstrapSample : MonoBehaviour
    {
        private const string SamplesSceneFileName = "Samples.unity";

        [Header("Credentials")]
        [SerializeField] private string gameAccessToken = "your-token";
        [SerializeField] private string gameId = "game-001";
        [SerializeField] private string userId = "player-001";
        [SerializeField] private string gameVersion = "1.0.0";

        [Header("Endpoints")]
        [SerializeField] private bool useLocalBackend = true;
        [SerializeField] private string localEndpoint = PlayServSettings.DefaultLocalEndpoint;
        [SerializeField] private string remoteEndpoint = PlayServSettings.DefaultRemoteEndpoint;

        [Header("Behavior")]
        [SerializeField] private bool autoConnect;
        [SerializeField] private bool showOverlay = true;
        [SerializeField] private bool disconnectOnDestroy = true;

        [Header("KeepAlive")]
        [SerializeField] private int keepAlivePingIntervalMs = 5000;
        [SerializeField] private int keepAlivePongTimeoutMs = 5000;

        private string _status = "Not configured";

        private void Start()
        {
            Configure();

            if (autoConnect)
                _ = ConnectAsync();
        }

        private void OnEnable()
        {
            PlayServ.OnTransportError += OnTransportError;
            PlayServ.OnKeepAlivePingSent += OnPingSent;
            PlayServ.OnKeepAlivePongReceived += OnPongReceived;
        }

        private void OnDisable()
        {
            PlayServ.OnTransportError -= OnTransportError;
            PlayServ.OnKeepAlivePingSent -= OnPingSent;
            PlayServ.OnKeepAlivePongReceived -= OnPongReceived;
        }

        private void OnDestroy()
        {
            if (disconnectOnDestroy)
                PlayServ.Disconnect();
        }

        [ContextMenu("Configure SDK")]
        public void Configure()
        {
            var settings = new PlayServSettings
            {
                GameAccessToken = gameAccessToken,
                GameId = gameId,
                UserId = userId,
                GameVersion = gameVersion,
                UseLocalBackend = useLocalBackend,
                LocalEndpoint = localEndpoint,
                RemoteEndpoint = remoteEndpoint,
                KeepAlivePingIntervalMs = keepAlivePingIntervalMs,
                KeepAlivePongTimeoutMs = keepAlivePongTimeoutMs
            };

            PlayServ.Config(settings);
            _status = $"Configured ({settings.Endpoint})";
            Debug.Log(
                $"[PlayServ][Sample] Configured. endpoint={settings.Endpoint}, pingInterval={settings.KeepAlivePingIntervalMs}ms, pongTimeout={settings.KeepAlivePongTimeoutMs}ms");
        }

        [ContextMenu("Connect SDK")]
        public void Connect()
        {
            _ = ConnectAsync();
        }

        [ContextMenu("Disconnect SDK")]
        public void Disconnect()
        {
            PlayServ.Disconnect();
            _status = "Disconnected";
        }

        public async Task ConnectAsync()
        {
            try
            {
                bool connected = await PlayServ.Connect();
                _status = connected ? "Connected" : "Connection failed";
            }
            catch (Exception ex)
            {
                _status = $"Connect error: {ex.Message}";
            }
        }

        private void OnTransportError(TransportError error)
        {
            _status = $"Transport error: {error}";
            Debug.LogError($"[PlayServ][Sample] Transport error: {error}");
        }

        private void OnPingSent()
        {
            _status = "KeepAlive ping sent";
            Debug.Log("[PlayServ][Sample] KeepAlive ping sent.");
        }

        private void OnPongReceived()
        {
            _status = "KeepAlive pong received";
            Debug.Log("[PlayServ][Sample] KeepAlive pong received.");
        }

        private void OnGUI()
        {
            if (!showOverlay)
                return;

            GUILayout.BeginArea(new Rect(10f, 10f, 460f, 220f), GUI.skin.box);
            GUILayout.Label("PlayServ Bootstrap Sample");
            GUILayout.Label($"State: {PlayServ.State}");
            GUILayout.Label($"Status: {_status}");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Configure"))
                Configure();
            if (GUILayout.Button("Connect"))
                _ = ConnectAsync();
            if (GUILayout.Button("Disconnect"))
                Disconnect();
            GUILayout.EndHorizontal();

            if (CanReturnToSamples())
            {
                GUILayout.Space(6f);
                if (GUILayout.Button("Back to Samples"))
                    ReturnToSamples();
            }

            GUILayout.EndArea();
        }

        private static bool CanReturnToSamples()
        {
            var scene = SceneManager.GetActiveScene();
            return scene.IsValid() && !string.Equals(scene.name, Path.GetFileNameWithoutExtension(SamplesSceneFileName),
                StringComparison.OrdinalIgnoreCase);
        }

        private void ReturnToSamples()
        {
            var scenePath = ResolveSamplesScenePath();

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                if (!File.Exists(scenePath))
                {
                    _status = $"Scene not found: {scenePath}";
                    return;
                }

                EditorSceneManager.OpenScene(scenePath);
                _status = "Opened 0_Samples";
                return;
            }
#endif

            SceneManager.LoadScene(Path.GetFileNameWithoutExtension(scenePath));
        }

        private static string ResolveSamplesScenePath()
        {
            var activePath = SceneManager.GetActiveScene().path;
            var activeDirectory = Path.GetDirectoryName(activePath);
            return string.IsNullOrEmpty(activeDirectory)
                ? SamplesSceneFileName
                : Path.Combine(activeDirectory, SamplesSceneFileName).Replace('\\', '/');
        }
    }
}
