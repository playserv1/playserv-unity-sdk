#if UNITY_5_3_OR_NEWER
using System.Threading.Tasks;
using UnityEngine;

namespace Playserv.Wrapper
{
    public interface IPlayServSpawnApi
    {
        Task<GameObject> Spawn(string assetName, Vector3 position, Quaternion rotation);

        Task<GameObject> Spawn(string assetName, Vector3 position);
    }
}
#endif
