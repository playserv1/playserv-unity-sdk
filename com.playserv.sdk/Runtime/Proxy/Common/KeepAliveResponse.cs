using System;
using UnityEngine;

namespace Playserv.Proxy.Common
{
    [Serializable]
    public sealed class KeepAliveResponse
    {
        [SerializeField]
        private long timestamp;

        public long Timestamp
        {
            get => timestamp;
            set => timestamp = value;
        }
    }
}
