using System;
using Playserv.Serialization;

namespace Playserv.Proxy.Common
{
    /// <summary>
    /// Response describing validation error on server side.
    /// </summary>
    [Serializable]
    public sealed class ValidationErrorResponse
    {
        [PlayServJsonName("error")]
        private string error;

        [PlayServJsonName("receivedJson")]
        private string receivedJson;

        /// <summary>
        /// Validation error message.
        /// </summary>
        public string Error => error;

        /// <summary>
        /// Raw JSON string that failed validation.
        /// </summary>
        public string ReceivedJson => receivedJson;
    }
}
