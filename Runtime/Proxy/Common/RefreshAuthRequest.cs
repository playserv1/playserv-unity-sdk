using System;

namespace Playserv.Proxy.Common
{
    /// <summary>
    /// Mid-connection player credential refresh request.
    /// </summary>
    [Serializable]
    public sealed class RefreshAuthRequest
    {
        /// <summary>
        /// Player session credential formatted as <c>Bearer &lt;jwt&gt;</c>.
        /// </summary>
        public string Authorization { get; set; }
    }
}
