using System;

namespace Playserv.RPC
{
    /// <summary>
    /// Transport command that maps to server command name "InvokeRpc".
    /// Class name is used by serializer and must stay exactly this.
    /// </summary>
    [Serializable]
    public sealed class InvokeRpc
    {
        public string ServiceName { get; set; } = string.Empty;
        public string MethodName { get; set; } = string.Empty;
        public string Payload { get; set; } = string.Empty;

        /// <summary>
        /// Optional client-provided coalesce key. When non-null, the platform's
        /// RpcExecutionWorker may drop queued instances of this command with
        /// matching (UserId × CoalesceKey) scope under overload — only the
        /// most recently enqueued one is executed; older ones get a synthetic
        /// success ack. Use for state-style RPCs where latest wins (e.g.
        /// SetInput with playerId as key). Leave null for event-style RPCs
        /// that must always run (Shoot, EnterBattle, abilities, etc.).
        /// </summary>
        public string CoalesceKey { get; set; }

        /// <summary>
        /// When true the gateway processes this call ONE-WAY: it forwards the invocation and returns
        /// NOTHING to the client (no InvokeRpcResponse, no game-server round-trip awaited on the read
        /// loop). Use for the ~15 Hz hot path (SetInput) so a per-command round-trip can't starve the
        /// connection's command stream. The gateway reads the field name verbatim ("FireAndForget");
        /// the caller must NOT rely on a reply (reconcile off the broadcast instead).
        /// </summary>
        public bool FireAndForget { get; set; }
    }
}
