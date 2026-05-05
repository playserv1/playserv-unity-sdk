#if UNITY_5_3_OR_NEWER && !PLAYSERV_DISABLE_SPAWN && !PLAYSERV_DISABLE_EVENTS
using System;
using System.Threading.Tasks;
using Playserv.Events;
using Playserv.Modules;
using UnityEngine;

namespace Playserv.Spawn
{
    public sealed class PlayServSpawnModule : IPlayServModule, IPlayServConnectionAwareModule, IPlayServSpawnModule
    {
        private readonly SpawnManager _spawnManager = new SpawnManager();
        private IEventsAdapter _eventsAdapter;

        public PlayServModuleDescriptor Descriptor { get; } = new PlayServModuleDescriptor(
            PlayServModuleIds.Spawn,
            isCore: false,
            PlayServModuleIds.Events);

        public void Initialize(PlayServModuleContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            _eventsAdapter = context.Services.Get<IEventsAdapter>();
            context.Services.Register<IPlayServSpawnModule>(this);
            context.Services.Register(this);
        }

        public void OnConnected()
        {
            _spawnManager.Initialize(_eventsAdapter.Publish, _eventsAdapter.Subscribe);
        }

        public Task<GameObject> SpawnAsync(string assetName, Vector3 position, Quaternion rotation)
        {
            return _spawnManager.SpawnAsync(assetName, position, rotation);
        }

        public bool TryGetSpawnedObject(string spawnId, out GameObject obj)
        {
            return _spawnManager.TryGetSpawnedObject(spawnId, out obj);
        }

        public NetworkObject GetNetworkObject(string networkId)
        {
            return _spawnManager.GetNetworkObject(networkId);
        }

        public void Shutdown()
        {
            _spawnManager.Dispose();
            _eventsAdapter = null;
        }
    }
}
#endif
