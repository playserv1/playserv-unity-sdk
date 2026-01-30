using System.Threading.Tasks;
using Playserv.Spawn;
using UnityEngine;

namespace Playserv.Proxy.Common
{
    public sealed partial class PlayServImplementation
    {
        private readonly SpawnManager _spawnManager = new();

        private void InitializeSpawnManager() =>
            _spawnManager.Initialize(Publish, Subscribe);

        public Task<GameObject> Spawn(string assetName, Vector3 position, Quaternion rotation) =>
            _spawnManager.SpawnAsync(assetName, position, rotation);

        public Task<GameObject> Spawn(string assetName, Vector3 position) =>
            Spawn(assetName, position, Quaternion.identity);

        private void DisposeSpawnManager() =>
            _spawnManager.Dispose();
    }
}
