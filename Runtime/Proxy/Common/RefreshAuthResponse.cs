using System;

namespace Playserv.Proxy.Common
{
    /// <summary>
    /// Response returned for <see cref="RefreshAuthRequest"/>.
    /// </summary>
    [Serializable]
    public sealed class RefreshAuthResponse
    {
        public bool success;
        public string message;
    }
}
