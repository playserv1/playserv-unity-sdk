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
    }
}
