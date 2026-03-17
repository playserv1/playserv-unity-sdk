using Newtonsoft.Json;

namespace Playserv.Proxy.Common
{
    /// <summary>
    /// Server-initiated forced disconnect payload.
    /// </summary>
    public sealed class ForcedDisconnectResponse
    {
        /// <summary>
        /// Success flag from server payload. Expected to be false for forced disconnect.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Transport error code (for forced disconnect expected 02003).
        /// </summary>
        public int ErrorCode { get; set; }

        /// <summary>
        /// Human-readable error message.
        /// </summary>
        public string ErrorMessage { get; set; } = string.Empty;

        /// <summary>
        /// Forced disconnect reason (Maintenance or GameVersionMismatch).
        /// </summary>
        public string Reason { get; set; } = string.Empty;

        /// <summary>
        /// Optional server game version.
        /// </summary>
        [JsonProperty("server_version")]
        public string ServerVersion { get; set; } = string.Empty;
    }
}
