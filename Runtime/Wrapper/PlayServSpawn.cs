#if UNITY_5_3_OR_NEWER
using System.Threading.Tasks;
using UnityEngine;

namespace Playserv.Wrapper
{
    /// <summary>
    /// Spawn-oriented PlayServ SDK surface.
    /// </summary>
    public static class PlayServSpawn
    {
        private static IPlayServSpawnApi Api => PlayServApiHost.Spawn;

        public static Task<GameObject> Spawn(string assetName, Vector3 position, Quaternion rotation) =>
            Api.Spawn(assetName, position, rotation);

        public static Task<GameObject> Spawn(string assetName, Vector3 position) =>
            Api.Spawn(assetName, position);
    }
}
#endif
