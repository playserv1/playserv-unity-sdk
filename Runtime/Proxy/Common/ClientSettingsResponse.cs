using System;

namespace Playserv.Proxy.Common
{
    /// <summary>
    /// Response containing server-applied client settings.
    /// </summary>
    [Serializable]
    public sealed class ClientSettingsResponse
    {
        private bool allowMultipleConnections;

        /// <summary>
        /// Effective multi-connection setting confirmed by server.
        /// </summary>
        public bool AllowMultipleConnections => allowMultipleConnections;
    }
}

