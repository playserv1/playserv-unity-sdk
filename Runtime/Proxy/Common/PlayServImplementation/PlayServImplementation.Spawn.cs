#if UNITY_5_3_OR_NEWER && !PLAYSERV_DISABLE_SPAWN && !PLAYSERV_DISABLE_EVENTS
using System.Threading.Tasks;
using Playserv.Spawn;
using UnityEngine;

namespace Playserv.Proxy.Common
{
    public sealed partial class PlayServImplementation
    {
        public Task<GameObject> Spawn(string assetName, Vector3 position, Quaternion rotation) =>
            GetModuleServices().Get<IPlayServSpawnModule>().SpawnAsync(assetName, position, rotation);

        public Task<GameObject> Spawn(string assetName, Vector3 position) =>
            Spawn(assetName, position, Quaternion.identity);
    }
}
#endif
