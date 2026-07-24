using System;

namespace Playserv.RPC
{
    /// <summary>
    /// Transport payload used to invoke server-side RPC method.
    /// </summary>
    [Serializable]
    public sealed class RpcInvokeRequest
    {
        /// <summary>
        /// Client-generated request identifier.
        /// </summary>
        public string RequestId { get; set; } = string.Empty;

        /// <summary>
        /// RPC service name on server side.
        /// </summary>
        public string ServiceName { get; set; } = string.Empty;

        /// <summary>
        /// Target method name in the service.
        /// </summary>
        public string MethodName { get; set; } = string.Empty;

        /// <summary>
        /// Base64-encoded UTF8 JSON payload.
        /// </summary>
        public string Payload { get; set; } = string.Empty;
    }
}
