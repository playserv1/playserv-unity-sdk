using System;

namespace Playserv.Proxy.Common
{
    /// <summary>
    /// Response describing JSON parse error on server side.
    /// </summary>
    [Serializable]
    public sealed class ParseErrorResponse
    {
        /// <summary>
        /// Parse error message.
        /// </summary>
        public string Error { get; set; }

        /// <summary>
        /// Raw JSON string that failed to parse.
        /// </summary>
        public string ReceivedJson { get; set; }
    }
}
