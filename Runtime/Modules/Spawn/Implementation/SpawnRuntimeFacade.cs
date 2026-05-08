using System;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Modules;
using Playserv.Wrapper;
using UnityEngine;

namespace Playserv.Spawn
{
    public sealed class SpawnRuntimeFacade
    {
        private readonly Func<IPlayServModuleServiceProvider> _getRequiredServices;
        private readonly Func<IPlayServModuleServiceProvider> _getCurrentServices;

        internal SpawnRuntimeFacade(IPlayServSpawnRuntimeAccess runtimeAccess)
            : this(
                () => runtimeAccess.RequiredServices,
                () => runtimeAccess.CurrentServices)
        {
            if (runtimeAccess == null)
                throw new ArgumentNullException(nameof(runtimeAccess));
        }

        public SpawnRuntimeFacade(
            Func<IPlayServModuleServiceProvider> getRequiredServices,
            Func<IPlayServModuleServiceProvider> getCurrentServices)
        {
            _getRequiredServices = getRequiredServices ?? throw new ArgumentNullException(nameof(getRequiredServices));
            _getCurrentServices = getCurrentServices ?? throw new ArgumentNullException(nameof(getCurrentServices));
        }

        public Task<GameObject> Spawn(string assetName, Vector3 position, Quaternion rotation) =>
            RequiredModule.SpawnAsync(assetName, position, rotation);

        public Task<GameObject> Spawn(string assetName, Vector3 position) =>
            Spawn(assetName, position, Quaternion.identity);

        public string CurrentScope => CurrentModule?.CurrentScope;

        public Task<bool> JoinScopeAsync(string groupName, CancellationToken ct = default) =>
            RequiredModule.JoinScopeAsync(groupName, ct);

        public Task<bool> LeaveScopeAsync(CancellationToken ct = default) =>
            RequiredModule.LeaveScopeAsync(ct);

        public bool Despawn(string spawnId) =>
            RequiredModule.Despawn(spawnId);

        public bool Despawn(GameObject instance) =>
            RequiredModule.Despawn(instance);

        public void SetPrefabRegistry(INetworkPrefabRegistry prefabRegistry)
        {
            CurrentModule?.SetPrefabRegistry(prefabRegistry);
        }

        public void SetTransformSyncIntervalMs(int intervalMs)
        {
            CurrentModule?.SetTransformSyncIntervalMs(intervalMs);
        }

        private IPlayServSpawnModule RequiredModule =>
            _getRequiredServices().Get<IPlayServSpawnModule>();

        private IPlayServSpawnModule CurrentModule
        {
            get
            {
                var services = _getCurrentServices();
                return services != null && services.TryGet<IPlayServSpawnModule>(out var module)
                    ? module
                    : null;
            }
        }
    }
}
