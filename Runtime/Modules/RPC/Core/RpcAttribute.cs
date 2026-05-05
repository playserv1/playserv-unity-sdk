#if !PLAYSERV_DISABLE_RPC_CORE
using System;

namespace Playserv.RPC
{
    /// <summary>
    /// Marks service class as available for PlayServ RPC invoke API.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class RpcAttribute : Attribute
    {
    }
}

#endif
