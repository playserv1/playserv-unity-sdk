using System;

namespace Playserv.Proxy.Common
{
    [Serializable]
    public sealed class ClientSettingsResponse
    {
        private bool allowMultipleConnections;

        public bool AllowMultipleConnections => allowMultipleConnections;
    }
}


