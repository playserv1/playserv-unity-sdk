using System;

namespace Playserv.RPC
{
    /// <summary>
    /// Options for an awaitable RPC invocation.
    /// </summary>
    public sealed class PlayServRpcInvokeOptions
    {
        /// <summary>
        /// Reject JSON type coercion in typed responses. Defaults to false. Unsupported codecs or
        /// response contracts return DeserializationFailed before sending. Captured when invoked.
        /// </summary>
        public bool StrictResponseTypes { get; set; }

        /// <summary>
        /// Default time an awaitable RPC waits for a response.
        /// </summary>
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Maximum time to wait for an InvokeRpcResponse.
        /// </summary>
        public TimeSpan Timeout { get; set; } = DefaultTimeout;

        /// <summary>
        /// Optional caller-provided request identifier. A unique identifier is generated when omitted.
        /// </summary>
        public string RequestId { get; set; }

        /// <summary>
        /// Optional queue coalescing key forwarded to the gateway.
        /// </summary>
        public string CoalesceKey { get; set; }
    }
}
