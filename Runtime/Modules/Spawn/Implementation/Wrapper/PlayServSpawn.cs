using System.Threading.Tasks;
using System.Threading;
using Playserv.Proxy.Common;
using Playserv.Spawn;
using UnityEngine;

namespace Playserv.Wrapper
{
    /// <summary>
    /// Spawn-oriented PlayServ SDK surface.
    /// </summary>
    public static class PlayServSpawn
    {
        private static readonly SpawnRuntimeFacade Api = new SpawnRuntimeFacade(
            () => PlayServRuntimeHost.RequiredInstance.ModuleServices,
            () => PlayServRuntimeHost.CurrentInstance?.ModuleServices);

        public static string CurrentScope => Api.CurrentScope;

        public static Task<GameObject> Spawn(string assetName, Vector3 position, Quaternion rotation) =>
            Api.Spawn(assetName, position, rotation);

        public static Task<GameObject> Spawn(string assetName, Vector3 position) =>
            Api.Spawn(assetName, position);

        public static Task<bool> JoinSpawnScopeAsync(string groupName, CancellationToken ct = default) =>
            Api.JoinScopeAsync(groupName, ct);

        public static Task<bool> JoinSpawnScope(string groupName, CancellationToken ct = default) =>
            Api.JoinScopeAsync(groupName, ct);

        public static Task<bool> LeaveSpawnScopeAsync(CancellationToken ct = default) =>
            Api.LeaveScopeAsync(ct);

        public static Task<bool> LeaveSpawnScope(CancellationToken ct = default) =>
            Api.LeaveScopeAsync(ct);

        public static bool Despawn(string spawnId) =>
            Api.Despawn(spawnId);

        public static bool Despawn(GameObject instance) =>
            Api.Despawn(instance);

        public static void SetPrefabRegistry(INetworkPrefabRegistry prefabRegistry) =>
            Api.SetPrefabRegistry(prefabRegistry);
    }
}
