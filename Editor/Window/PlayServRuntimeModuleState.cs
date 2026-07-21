using System;
using System.Collections.Generic;
using Playserv.Modules;

namespace Playserv.Editor
{
    internal sealed class PlayServRuntimeModuleState
    {
        private readonly HashSet<string> _enabledModuleIds;

        public PlayServRuntimeModuleState()
        {
            _enabledModuleIds = new HashSet<string>(StringComparer.Ordinal);
        }

        private PlayServRuntimeModuleState(IEnumerable<string> enabledModuleIds)
        {
            _enabledModuleIds = new HashSet<string>(enabledModuleIds, StringComparer.Ordinal);
        }

        public bool IsEnabled(string moduleId)
        {
            return TryGetEnabled(moduleId, out var enabled) && enabled;
        }

        public bool TryGetEnabled(string moduleId, out bool enabled)
        {
            if (!PlayServModuleManifest.TryGet(moduleId, out _))
            {
                enabled = false;
                return false;
            }

            enabled = _enabledModuleIds.Contains(moduleId);
            return true;
        }

        public void SetEnabled(string moduleId, bool enabled)
        {
            if (!TrySetEnabled(moduleId, enabled))
                throw new ArgumentException($"Unknown PlayServ runtime module id: {moduleId}", nameof(moduleId));
        }

        public bool TrySetEnabled(string moduleId, bool enabled)
        {
            if (!PlayServModuleManifest.TryGet(moduleId, out _))
                return false;

            if (enabled)
                _enabledModuleIds.Add(moduleId);
            else
                _enabledModuleIds.Remove(moduleId);

            return true;
        }

        public PlayServRuntimeModuleState Clone()
        {
            return new PlayServRuntimeModuleState(_enabledModuleIds);
        }

        public bool HasSameEnabledModules(PlayServRuntimeModuleState other)
        {
            if (other == null)
                return false;

            foreach (var module in PlayServModuleManifest.RuntimeModules)
            {
                if (IsEnabled(module.Id) != other.IsEnabled(module.Id))
                    return false;
            }

            return true;
        }
    }
}
