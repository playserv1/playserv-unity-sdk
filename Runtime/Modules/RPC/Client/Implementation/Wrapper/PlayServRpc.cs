using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Playserv.Proxy.Common;
using Playserv.RPC;

namespace Playserv.Wrapper
{
    /// <summary>
    /// RPC and low-level command surface for PlayServ SDK.
    /// </summary>
    public static partial class PlayServRpc
    {
        private static readonly IPlayServRpcApi Api = new PlayServApiRpcFacade();

        public static event Action<InvokeRpcResponse> OnRpcInvokeResponse
        {
            add => Api.OnRpcInvokeResponse += value;
            remove => Api.OnRpcInvokeResponse -= value;
        }

        public static void Send<T>(T command) => Api.Send(command);

        public static void Send<T>(T command, string moduleName) => Api.Send(command, moduleName);

        public static void Invoke(string serviceName, string methodName, object payload) =>
            Api.Invoke(serviceName, methodName, payload);

        public static void InvokeArgs(string serviceName, string methodName, params object[] args) =>
            Api.InvokeArgs(serviceName, methodName, args);

        public static void InvokeNamed(string serviceName, string methodName, IDictionary<string, object> payload) =>
            Api.InvokeNamed(serviceName, methodName, payload);

        public static void Invoke(string serviceName, string methodName, string payloadBase64) =>
            Api.Invoke(serviceName, methodName, payloadBase64);

        public static void Invoke<TService>(Expression<Action<TService>> method) =>
            Api.Invoke(method);

        public static void Invoke<TService>(Expression<Action<TService>> method, object payload) =>
            Api.Invoke(method, payload);

        public static void Invoke<TService>(Expression<Action<TService>> method, string payloadBase64) =>
            Api.Invoke(method, payloadBase64);
    }
}
