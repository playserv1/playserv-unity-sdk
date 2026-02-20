using System;
using System.Threading.Tasks;
using Playserv.Spawn;
using Playserv.Wrapper;
using UnityEngine;

namespace Playserv.Samples
{
    /// <summary>
    /// Example for network spawn API.
    /// </summary>
    public sealed class PlayServSpawnSample : MonoBehaviour
    {
        [Header("Prefab")]
        [SerializeField] private string assetName = "TestCube";

        [Header("Spawn Area")]
        [SerializeField] private Vector3 spawnCenter = Vector3.zero;
        [SerializeField] private float spawnRadius = 6f;

        [Header("Behavior")]
        [SerializeField] private bool showOverlay = true;

        private GameObject _lastSpawned;
        private string _status = "Idle";

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
                var instance = await PlayServ.Spawn(assetName, position, rotation);

                if (instance == null)
                {
                    _status = "Spawn failed (check Resources path and components)";
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
            }
            catch (Exception ex)
            {
                _status = $"Spawn error: {ex.Message}";
            }
        }

        private void OnGUI()
        {
            if (!showOverlay)
                return;

            GUILayout.BeginArea(new Rect(10f, 280f, 420f, 180f), GUI.skin.box);
            GUILayout.Label("PlayServ Spawn Sample");
            GUILayout.Label($"State: {PlayServ.State}");
            GUILayout.Label($"Status: {_status}");

            if (GUILayout.Button("Spawn Random"))
                _ = SpawnRandomAsync();

            if (_lastSpawned != null)
                GUILayout.Label($"Last spawned: {_lastSpawned.name}");

            GUILayout.EndArea();
        }
    }
}
