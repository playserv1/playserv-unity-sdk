namespace Playserv.RPC
{
    /// <summary>
    /// Contract for in-process RPC invocation used by PlayServRpc.Invoke.
    /// </summary>
    public interface IRpcInvoker
    {
        /// <summary>
        /// Tries to invoke RPC method in-process.
        /// </summary>
        /// <param name="serviceName">RPC service name.</param>
        /// <param name="methodName">RPC method name.</param>
        /// <param name="payloadBase64">Base64-encoded UTF8 JSON payload.</param>
        /// <returns>True when invocation was handled locally; otherwise false.</returns>
        bool TryInvoke(string serviceName, string methodName, string payloadBase64);
    }
}
