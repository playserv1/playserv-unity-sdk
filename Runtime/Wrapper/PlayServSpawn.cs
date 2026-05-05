#if UNITY_5_3_OR_NEWER && !PLAYSERV_DISABLE_SPAWN && !PLAYSERV_DISABLE_EVENTS
using System.Threading.Tasks;
using System.Threading;
using Playserv.Spawn;
using UnityEngine;

namespace Playserv.Wrapper
{
    /// <summary>
    /// Spawn-oriented PlayServ SDK surface.
    /// </summary>
    public static class PlayServSpawn
    {
        private static IPlayServSpawnApi Api => PlayServApiHost.Spawn;

        public static string CurrentScope => Api.CurrentSpawnScope;

        public static Task<GameObject> Spawn(string assetName, Vector3 position, Quaternion rotation) =>
            Api.Spawn(assetName, position, rotation);

        public static Task<GameObject> Spawn(string assetName, Vector3 position) =>
            Api.Spawn(assetName, position);

        public static Task<bool> JoinSpawnScopeAsync(string groupName, CancellationToken ct = default) =>
            Api.JoinSpawnScopeAsync(groupName, ct);

        public static Task<bool> JoinSpawnScope(string groupName, CancellationToken ct = default) =>
            Api.JoinSpawnScopeAsync(groupName, ct);

        public static Task<bool> LeaveSpawnScopeAsync(CancellationToken ct = default) =>
            Api.LeaveSpawnScopeAsync(ct);

        public static Task<bool> LeaveSpawnScope(CancellationToken ct = default) =>
            Api.LeaveSpawnScopeAsync(ct);

        public static bool Despawn(string spawnId) =>
            Api.Despawn(spawnId);

        public static bool Despawn(GameObject instance) =>
            Api.Despawn(instance);

        public static void SetPrefabRegistry(INetworkPrefabRegistry prefabRegistry) =>
            Api.SetSpawnPrefabRegistry(prefabRegistry);
    }
}
#endif
