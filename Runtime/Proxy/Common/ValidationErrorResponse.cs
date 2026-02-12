using System;

namespace Playserv.Proxy.Common
{
    /// <summary>
    /// Response describing validation error on server side.
    /// </summary>
    [Serializable]
    public sealed class ValidationErrorResponse
    {
        private string error;
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
