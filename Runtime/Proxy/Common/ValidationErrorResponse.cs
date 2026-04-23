using System;

namespace Playserv.Proxy.Common
{
    /// <summary>
    /// Response describing validation error on server side.
    /// </summary>
    [Serializable]
    public sealed class ValidationErrorResponse
    {
        /// <summary>
        /// Validation error message.
        /// </summary>
        public string Error { get; set; }

        /// <summary>
        /// Raw JSON string that failed validation.
        /// </summary>
        public string ReceivedJson { get; set; }
    }
}
