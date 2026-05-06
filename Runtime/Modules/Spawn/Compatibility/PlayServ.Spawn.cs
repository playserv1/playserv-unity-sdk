#if UNITY_5_3_OR_NEWER && !PLAYSERV_MODULE_DISABLED_SPAWN && !PLAYSERV_MODULE_DISABLED_EVENTS
using System.Threading;
using System.Threading.Tasks;
using Playserv.Spawn;
using UnityEngine;

namespace Playserv.Wrapper
{
    public static partial class PlayServ
    {
        public static Task<GameObject> Spawn(string assetName, Vector3 position, Quaternion rotation) =>
            PlayServSpawn.Spawn(assetName, position, rotation);

        public static Task<GameObject> Spawn(string assetName, Vector3 position) =>
            PlayServSpawn.Spawn(assetName, position);

        public static string CurrentSpawnScope => PlayServSpawn.CurrentScope;

        public static Task<bool> JoinSpawnScopeAsync(string groupName, CancellationToken ct = default) =>
            PlayServSpawn.JoinSpawnScopeAsync(groupName, ct);

        public static Task<bool> JoinSpawnScope(string groupName, CancellationToken ct = default) =>
            PlayServSpawn.JoinSpawnScope(groupName, ct);

        public static Task<bool> LeaveSpawnScopeAsync(CancellationToken ct = default) =>
            PlayServSpawn.LeaveSpawnScopeAsync(ct);

        public static Task<bool> LeaveSpawnScope(CancellationToken ct = default) =>
            PlayServSpawn.LeaveSpawnScope(ct);

        public static bool Despawn(string spawnId) =>
            PlayServSpawn.Despawn(spawnId);

        public static bool Despawn(GameObject instance) =>
            PlayServSpawn.Despawn(instance);

        public static void SetSpawnPrefabRegistry(INetworkPrefabRegistry prefabRegistry) =>
            PlayServSpawn.SetPrefabRegistry(prefabRegistry);
    }
}
#endif
