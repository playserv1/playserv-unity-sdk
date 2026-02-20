using System;

namespace Playserv.Proxy.Common
{
    /// <summary>
    /// Generic command-router error payload returned by backend with command name "error".
    /// </summary>
    [Serializable]
    public sealed class CommandErrorResponse
    {
        public string error;
        public string message;
        public string timestamp;

        /// <summary>
        /// Short backend error category (for example "Unknown command").
        /// </summary>
        public string Error => error;

        /// <summary>
        /// Human-readable backend error message.
        /// </summary>
        public string Message => message;

        /// <summary>
        /// Backend timestamp in ISO format when available.
        /// </summary>
        public string Timestamp => timestamp;
    }
}
