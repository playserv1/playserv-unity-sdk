using System;

namespace Playserv.Proxy.Common
{
    [Serializable]
    public sealed class HandshakeRequest
    {
        public string GameAccessToken { get; set; }
        public string SdkVersion { get; set; }
        public string GameVersion { get; set; }
    }
}
