#if UNITY_5_3_OR_NEWER
using System.Threading.Tasks;
using System.Threading;
using UnityEngine;

namespace Playserv.Wrapper
{
    public interface IPlayServSpawnApi
    {
        Task<GameObject> Spawn(string assetName, Vector3 position, Quaternion rotation);

        Task<GameObject> Spawn(string assetName, Vector3 position);

        string CurrentSpawnScope { get; }

        Task<bool> JoinSpawnScopeAsync(string groupName, CancellationToken ct = default);

        Task<bool> LeaveSpawnScopeAsync(CancellationToken ct = default);

        bool Despawn(string spawnId);

        bool Despawn(GameObject instance);

    }
}
#endif
