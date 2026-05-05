#if UNITY_5_3_OR_NEWER && !PLAYSERV_DISABLE_SPAWN && !PLAYSERV_DISABLE_EVENTS
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
        /// User id of the client that owns this object.
        /// </summary>
        public string OwnerId { get; private set; }

        /// <summary>
        /// Event group that owns spawn and transform routing for this object.
        /// </summary>
        public string ScopeGroupName { get; private set; }

        /// <summary>
        /// Indicates whether this object was spawned by local client.
        /// </summary>
        public bool IsLocallyOwned { get; private set; }

        internal void SetNetworkId(string networkId, string ownerId, bool isLocallyOwned, string scopeGroupName)
        {
            NetworkId = networkId;
            OwnerId = ownerId ?? string.Empty;
            IsLocallyOwned = isLocallyOwned;
            ScopeGroupName = string.IsNullOrWhiteSpace(scopeGroupName) ? string.Empty : scopeGroupName.Trim();
        }
    }
}
#endif
