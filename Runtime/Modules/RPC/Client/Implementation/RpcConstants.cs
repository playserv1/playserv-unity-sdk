namespace Playserv.RPC
{
    /// <summary>
    /// RPC-related command/module names used by PlayServ transport.
    /// </summary>
    public static class RpcConstants
    {
        /// <summary>
        /// Target module service id for RPC commands.
        /// </summary>
        public const string InvokeModuleServiceName = "rpc";

        /// <summary>
        /// Command name expected by rpc module command router.
        /// </summary>
        public const string InvokeCommandName = "InvokeRpc";

        /// <summary>
        /// Legacy combined module path kept for backward compatibility references.
        /// </summary>
        public const string InvokeModuleName = "rpc.InvokeRpc";
    }
}
