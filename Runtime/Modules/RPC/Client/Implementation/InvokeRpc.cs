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
    }
}
