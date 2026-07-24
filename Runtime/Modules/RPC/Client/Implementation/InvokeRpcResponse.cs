using System;

namespace Playserv.RPC
{
    /// <summary>
    /// Response payload for "InvokeRpcResponse" command sent by rpc module.
    /// </summary>
    public sealed class InvokeRpcResponse
    {
        /// <summary>
        /// Client request identifier. Newer gateways may echo it at the response root.
        /// </summary>
        public string RequestId { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTimeOffset Timestamp { get; set; }
        public InvokeRpcRequestInfo Request { get; set; }
        public string Result { get; set; }
    }

    /// <summary>
    /// Echoed request metadata from rpc module response.
    /// </summary>
    public sealed class InvokeRpcRequestInfo
    {
        /// <summary>
        /// Client request identifier echoed by the gateway.
        /// </summary>
        public string RequestId { get; set; } = string.Empty;

        public string ServiceName { get; set; } = string.Empty;
        public string MethodName { get; set; } = string.Empty;
    }
}
