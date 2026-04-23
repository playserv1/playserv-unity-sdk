using System;
using Playserv.Serialization;

namespace Playserv.Proxy.Common
{
    /// <summary>
    /// Response describing JSON parse error on server side.
    /// </summary>
    [Serializable]
    public sealed class ParseErrorResponse
    {
        [PlayServJsonName("error")]
        private string error;

        [PlayServJsonName("receivedJson")]
        private string receivedJson;

        /// <summary>
        /// Parse error message.
        /// </summary>
        public string Error => error;

        /// <summary>
        /// Raw JSON string that failed to parse.
        /// </summary>
        public string ReceivedJson => receivedJson;
    }
}
