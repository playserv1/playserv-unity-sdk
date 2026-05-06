using System;
using Playserv.Events;

namespace Playserv.Spawn
{
    internal sealed class SpawnEventPublisher
    {
        private IEventsAdapter _eventsAdapter;
        private Func<string> _ownerIdProvider;
        private string _defaultScopeGroupName;

        public string DefaultScopeGroupName => _defaultScopeGroupName;

        public void Initialize(IEventsAdapter eventsAdapter, Func<string> ownerIdProvider)
        {
            _eventsAdapter = eventsAdapter ?? throw new ArgumentNullException(nameof(eventsAdapter));
            _ownerIdProvider = ownerIdProvider ?? (() => string.Empty);
        }

        public void SetDefaultScope(string groupName)
        {
            _defaultScopeGroupName = NormalizeScopeGroupName(groupName);
        }

        public void ClearDefaultScope()
        {
            _defaultScopeGroupName = null;
        }

        public void PublishSpawn(SpawnEvent spawnEvent)
        {
            if (TryGetDefaultScopeGroupName(out var groupName))
            {
                spawnEvent.ScopeGroupName = groupName;
                RequireEventsAdapter().PublishForGroup(groupName, spawnEvent);
                return;
            }

            spawnEvent.ScopeGroupName = string.Empty;
            RequireEventsAdapter().Publish(spawnEvent);
        }

        public void PublishDespawn(DespawnEvent despawnEvent)
        {
            var groupName = NormalizeScopeGroupName(despawnEvent.ScopeGroupName);
            if (!string.IsNullOrEmpty(groupName))
            {
                despawnEvent.ScopeGroupName = groupName;
                RequireEventsAdapter().PublishForGroup(groupName, despawnEvent);
                return;
            }

            despawnEvent.ScopeGroupName = string.Empty;
            RequireEventsAdapter().Publish(despawnEvent);
        }

        public void PublishScopeJoined(string groupName)
        {
            groupName = NormalizeScopeGroupName(groupName);
            if (string.IsNullOrEmpty(groupName) || _eventsAdapter == null)
                return;

            _eventsAdapter.PublishForGroup(groupName, new SpawnScopeJoinedEvent(groupName, ResolveOwnerId()));
        }

        public void PublishSpawnToGroup(string groupName, SpawnEvent spawnEvent)
        {
            groupName = NormalizeScopeGroupName(groupName);
            if (string.IsNullOrEmpty(groupName) || _eventsAdapter == null)
                return;

            spawnEvent.ScopeGroupName = groupName;
            _eventsAdapter.PublishForGroup(groupName, spawnEvent);
        }

        public string ResolveSpawnEventScope(SpawnEvent spawnEvent)
        {
            var scopeGroupName = NormalizeScopeGroupName(spawnEvent.ScopeGroupName);
            if (!string.IsNullOrEmpty(scopeGroupName))
                return scopeGroupName;

            return _defaultScopeGroupName ?? string.Empty;
        }

        public void Clear()
        {
            _eventsAdapter = null;
            _ownerIdProvider = null;
            _defaultScopeGroupName = null;
        }

        public static string NormalizeScopeGroupName(string groupName)
        {
            return string.IsNullOrWhiteSpace(groupName) ? null : groupName.Trim();
        }

        private bool TryGetDefaultScopeGroupName(out string groupName)
        {
            groupName = _defaultScopeGroupName;
            return !string.IsNullOrEmpty(groupName);
        }

        private string ResolveOwnerId()
        {
            return _ownerIdProvider?.Invoke() ?? string.Empty;
        }

        private IEventsAdapter RequireEventsAdapter()
        {
            if (_eventsAdapter == null)
                throw new InvalidOperationException("Spawn event publisher is not initialized.");

            return _eventsAdapter;
        }
    }
}
