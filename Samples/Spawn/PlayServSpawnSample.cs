using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Playserv.Spawn;
using Playserv.Wrapper;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace Playserv.Samples
{
    /// <summary>
    /// Example for network spawn API.
    /// </summary>
    public sealed class PlayServSpawnSample : MonoBehaviour
    {
        private const string SamplesSceneFileName = "Samples.unity";

        [Header("Prefab")]
        [SerializeField] private string assetName = "TestCube";

        [Header("Spawn Area")]
        [SerializeField] private Vector3 spawnCenter = Vector3.zero;
        [SerializeField] private float spawnRadius = 6f;

        [Header("Behavior")]
        [SerializeField] private bool showOverlay = true;

        private readonly List<string> _history = new List<string>();
        private GameObject _lastSpawned;
        private string _status = "Idle";
        private Vector2 _historyScroll;
        private bool _showInfo;

        [ContextMenu("Spawn Random")]
        public void SpawnRandom()
        {
            _ = SpawnRandomAsync();
        }

        public async Task SpawnRandomAsync()
        {
            try
            {
                var position = spawnCenter + UnityEngine.Random.insideUnitSphere * spawnRadius;
                position.y = Mathf.Max(0f, position.y);
                var rotation = UnityEngine.Random.rotation;

                _status = $"Spawning '{assetName}'...";
                AddLog(_status);
                var instance = await PlayServ.Spawn(assetName, position, rotation);

                if (instance == null)
                {
                    _status = "Spawn failed (check Resources path and components)";
                    AddLog(_status);
                    return;
                }

                _lastSpawned = instance;
                var networkObject = instance.GetComponent<NetworkObject>();

                if (networkObject != null)
                {
                    _status = $"Spawned id={networkObject.NetworkId}, local={networkObject.IsLocallyOwned}";
                }
                else
                {
                    _status = "Spawned object has no NetworkObject";
                }

                AddLog($"{_status}; object={instance.name}");
            }
            catch (Exception ex)
            {
                _status = $"Spawn error: {ex.Message}";
                AddLog(_status);
            }
        }

        private async Task ConnectSdkAsync()
        {
            try
            {
                var connected = await PlayServ.Connect();
                _status = connected ? "SDK connected" : "SDK connection failed";
                AddLog(_status);
            }
            catch (Exception ex)
            {
                _status = $"Connect error: {ex.Message}";
                AddLog(_status);
            }
        }

        private void DisconnectSdk()
        {
            PlayServ.Disconnect();
            _status = "SDK disconnected";
            AddLog(_status);
        }

        private void BackToSamples()
        {
            var scenePath = ResolveSamplesScenePath();
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                if (File.Exists(scenePath))
                    EditorSceneManager.OpenScene(scenePath);
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

        private void AddLog(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            _history.Add(line);
            if (_history.Count > 128)
                _history.RemoveAt(0);

            _historyScroll.y = float.MaxValue;
        }

        private void OnGUI()
        {
            if (!showOverlay)
                return;

            var margin = 10f;
            var areaWidth = Mathf.Max(320f, Screen.width - margin * 2f);
            var areaHeight = Mathf.Max(220f, Screen.height - margin * 2f);

            GUILayout.BeginArea(new Rect(margin, margin, areaWidth, areaHeight), GUI.skin.box);
            GUILayout.Label("PlayServ Spawn Sample");
            GUILayout.Label("How to use: connect SDK, ensure prefab is in Resources with NetworkObject component, then click Spawn Random.");
            GUILayout.Label($"SDK state: {PlayServ.State}");
            GUILayout.Label($"Status: {_status}");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Connect SDK"))
                _ = ConnectSdkAsync();
            if (GUILayout.Button("Disconnect SDK"))
                DisconnectSdk();
            if (GUILayout.Button("Back to 0_Samples"))
                BackToSamples();
            if (GUILayout.Button(_showInfo ? "Hide Info" : "Info"))
                _showInfo = !_showInfo;
            GUILayout.EndHorizontal();

            if (_showInfo)
            {
                GUILayout.Space(6f);
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label("Info");
                GUILayout.Label("Purpose: Demonstrates network spawn of prefabs from Resources.");
                GUILayout.Label("How to use: Connect, set assetName, click Spawn Random, then inspect spawned object state.");
                GUILayout.Label("Use in your game: Spawn players, bots, loot, and runtime world objects with network identity.");
                GUILayout.EndVertical();
            }

            if (GUILayout.Button("Spawn Random"))
                _ = SpawnRandomAsync();

            if (_lastSpawned != null)
                GUILayout.Label($"Last spawned: {_lastSpawned.name}");

            GUILayout.Space(8f);
            GUILayout.Label($"Logs ({_history.Count}):");
            _historyScroll = GUILayout.BeginScrollView(_historyScroll, GUILayout.ExpandHeight(true));
            if (_history.Count == 0)
            {
                GUILayout.Label("- No logs yet");
            }
            else
            {
                foreach (var line in _history)
                    GUILayout.Label($"- {line}");
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}
