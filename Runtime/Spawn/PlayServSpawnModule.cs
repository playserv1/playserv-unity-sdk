#if UNITY_5_3_OR_NEWER && !PLAYSERV_DISABLE_SPAWN && !PLAYSERV_DISABLE_EVENTS
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Events;
using Playserv.Modules;
using UnityEngine;

namespace Playserv.Spawn
{
    public sealed class PlayServSpawnModule : IPlayServModule, IPlayServConnectionAwareModule, IPlayServSpawnModule
    {
        private readonly SpawnManager _spawnManager = new SpawnManager();
        private readonly HashSet<string> _joinedScopeGroupNames = new HashSet<string>(StringComparer.Ordinal);
        private IEventsAdapter _eventsAdapter;
        private IPlayServRuntimeIdentity _runtimeIdentity;
        private string _defaultScopeGroupName;

        public PlayServModuleDescriptor Descriptor { get; } = new PlayServModuleDescriptor(
            PlayServModuleIds.Spawn,
            isCore: false,
            PlayServModuleIds.Events);

        public void Initialize(PlayServModuleContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            _eventsAdapter = context.Services.Get<IEventsAdapter>();
            _runtimeIdentity = context.Services.Get<IPlayServRuntimeIdentity>();
            context.Services.Register<IPlayServSpawnModule>(this);
            context.Services.Register(this);
        }

        public void OnConnected()
        {
            _spawnManager.Initialize(_eventsAdapter, () => _runtimeIdentity?.UserId ?? string.Empty);
        }

        public Task<GameObject> SpawnAsync(string assetName, Vector3 position, Quaternion rotation)
        {
            return _spawnManager.SpawnAsync(assetName, position, rotation);
        }

        public string CurrentScope => _defaultScopeGroupName;

        public async Task<bool> JoinScopeAsync(string groupName, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(groupName))
                throw new ArgumentException("Spawn scope group name is required.", nameof(groupName));

            var normalizedGroupName = groupName.Trim();
            if (_joinedScopeGroupNames.Contains(normalizedGroupName))
            {
                SetDefaultScope(normalizedGroupName);
                return true;
            }

            var subscribed = await _eventsAdapter.SubscribeGroupAsync(normalizedGroupName, ct).ConfigureAwait(false);
            if (!subscribed)
                return false;

            _joinedScopeGroupNames.Add(normalizedGroupName);
            SetDefaultScope(normalizedGroupName);
            _spawnManager.PublishScopeJoined(normalizedGroupName);
            return true;
        }

        public async Task<bool> LeaveScopeAsync(CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(_defaultScopeGroupName))
                return true;

            var groupName = _defaultScopeGroupName;
            var unsubscribed = await _eventsAdapter.UnsubscribeGroupAsync(groupName, ct).ConfigureAwait(false);
            if (!unsubscribed)
                return false;

            _joinedScopeGroupNames.Remove(groupName);
            SetDefaultScope(GetNextJoinedScopeOrNull());
            return true;
        }

        public bool TryGetSpawnedObject(string spawnId, out GameObject obj)
        {
            return _spawnManager.TryGetSpawnedObject(spawnId, out obj);
        }

        public bool Despawn(string spawnId)
        {
            return _spawnManager.Despawn(spawnId);
        }

        public bool Despawn(GameObject instance)
        {
            return _spawnManager.Despawn(instance);
        }

        public void SetPrefabRegistry(INetworkPrefabRegistry prefabRegistry)
        {
            _spawnManager.SetPrefabRegistry(prefabRegistry);
        }

        public NetworkObject GetNetworkObject(string networkId)
        {
            return _spawnManager.GetNetworkObject(networkId);
        }

        public void Shutdown()
        {
            _spawnManager.Dispose();
            _joinedScopeGroupNames.Clear();
            _defaultScopeGroupName = null;
            _eventsAdapter = null;
            _runtimeIdentity = null;
        }

        private void SetDefaultScope(string groupName)
        {
            _defaultScopeGroupName = groupName;
            _spawnManager.SetDefaultScope(groupName);
        }

        private string GetNextJoinedScopeOrNull()
        {
            foreach (var groupName in _joinedScopeGroupNames)
                return groupName;

            return null;
        }
    }
}
#endif
