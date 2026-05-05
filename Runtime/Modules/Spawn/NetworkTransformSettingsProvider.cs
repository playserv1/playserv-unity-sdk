#if UNITY_5_3_OR_NEWER && !PLAYSERV_DISABLE_SPAWN && !PLAYSERV_DISABLE_EVENTS
using Playserv.Wrapper;
using UnityEngine;

namespace Playserv.Spawn
{
    internal static class NetworkTransformSettingsProvider
    {
        private const int DefaultSyncIntervalMs = 50;

        public static float ResolveSyncIntervalSeconds()
        {
            var syncIntervalMs = ResolveSyncIntervalMs();
            return Mathf.Max(1, syncIntervalMs) / 1000f;
        }

        private static int ResolveSyncIntervalMs()
        {
            var config = Resources.Load<PlayServConfig>("PlayServConfig");
            if (config != null)
            {
#if UNITY_EDITOR
                return PlayServSettingsResolver.ResolveEditorSettings(config).NetworkTransformSyncIntervalMs;
#else
                return config.NetworkTransformSyncIntervalMs;
#endif
            }

            return PlayServPackageDefaultsProvider.TryLoadSettings(out var packageDefaults)
                ? packageDefaults.NetworkTransformSyncIntervalMs
                : DefaultSyncIntervalMs;
        }
    }
}

#endif
