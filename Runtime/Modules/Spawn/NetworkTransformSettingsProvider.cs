using UnityEngine;

namespace Playserv.Spawn
{
    internal static class NetworkTransformSettingsProvider
    {
        public static float ResolveSyncIntervalSeconds()
        {
            var syncIntervalMs = PlayServSpawnRuntime.TransformSyncIntervalMs;
            return Mathf.Max(1, syncIntervalMs) / 1000f;
        }
    }
}
