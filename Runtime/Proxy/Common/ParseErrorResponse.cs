using System;
using Newtonsoft.Json;

namespace Playserv.Proxy.Common
{
    /// <summary>
    /// Response describing JSON parse error on server side.
    /// </summary>
    [Serializable]
    public sealed class ParseErrorResponse
    {
        [JsonProperty("error")]
        private string error;

        [JsonProperty("receivedJson")]
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
