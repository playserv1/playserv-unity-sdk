#if UNITY_5_3_OR_NEWER
using UnityEngine;

namespace Playserv.Spawn
{
    public sealed class NetworkObject : MonoBehaviour
    {
        public string NetworkId { get; private set; }
        public bool IsLocallyOwned { get; private set; }

        internal void SetNetworkId(string networkId, bool isLocallyOwned)
        {
            NetworkId = networkId;
            IsLocallyOwned = isLocallyOwned;
        }
    }
}
#endif
