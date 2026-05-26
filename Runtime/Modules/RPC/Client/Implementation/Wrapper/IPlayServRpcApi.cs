using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Playserv.Proxy.Common;
using Playserv.RPC;

namespace Playserv.Wrapper
{
    public interface IPlayServRpcApi
    {
        event Action<InvokeRpcResponse> OnRpcInvokeResponse;

        void Send<T>(T command);

        void Send<T>(T command, string moduleName);

        void Invoke(string serviceName, string methodName, object payload);

        void InvokeArgs(string serviceName, string methodName, params object[] args);

        void InvokeNamed(string serviceName, string methodName, IDictionary<string, object> payload);

        void Invoke(string serviceName, string methodName, string payloadBase64);

        /// <summary>
        /// Invoke an RPC with an optional coalesce key. When the platform's
        /// queue contains multiple invocations of the same method from the
        /// same user with a matching <paramref name="coalesceKey"/>, only the
        /// most recent one will be executed and older ones get a synthetic
        /// success ack. Pass null to keep the original "execute every call"
        /// semantics. See <see cref="Playserv.RPC.InvokeRpc.CoalesceKey"/>.
        /// </summary>
        void Invoke(string serviceName, string methodName, object payload, string coalesceKey);

        void Invoke<TService>(Expression<Action<TService>> method);

        void Invoke<TService>(Expression<Action<TService>> method, object payload);

        void Invoke<TService>(Expression<Action<TService>> method, string payloadBase64);
    }
}
