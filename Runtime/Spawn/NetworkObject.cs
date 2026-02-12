#if UNITY_5_3_OR_NEWER
using UnityEngine;

namespace Playserv.Spawn
{
    /// <summary>
    /// Marks spawned GameObject as network-aware and stores runtime ownership metadata.
    /// </summary>
    public sealed class NetworkObject : MonoBehaviour
    {
        /// <summary>
        /// Unique network id assigned by spawn flow.
        /// </summary>
        public string NetworkId { get; private set; }

        /// <summary>
        /// Indicates whether this object was spawned by local client.
        /// </summary>
        public bool IsLocallyOwned { get; private set; }

        internal void SetNetworkId(string networkId, bool isLocallyOwned)
        {
            NetworkId = networkId;
            IsLocallyOwned = isLocallyOwned;
        }
    }
}
#endif
