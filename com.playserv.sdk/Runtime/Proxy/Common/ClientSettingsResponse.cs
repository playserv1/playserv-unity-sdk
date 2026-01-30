using System;
using UnityEngine;

namespace Playserv.Proxy.Common
{
    [Serializable]
    public sealed class ClientSettingsResponse
    {
        [SerializeField]
        private bool allowMultipleConnections;

        public bool AllowMultipleConnections => allowMultipleConnections;
    }
}


