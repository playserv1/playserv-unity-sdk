#if !PLAYSERV_MODULE_DISABLED_RPC_CORE && !PLAYSERV_MODULE_DISABLED_CLIENT_RPC
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Playserv.RPC;
#if !PLAYSERV_MODULE_DISABLED_LOCAL_EXECUTION_CORE && !PLAYSERV_MODULE_DISABLED_LOCAL_EXECUTION_SERVER
using Playserv.Server;
#endif

namespace Playserv.Wrapper
{
    public static partial class PlayServ
    {
        /// <summary>
        /// Raised when RPC module returns InvokeRpcResponse command.
        /// </summary>
        public static event Action<InvokeRpcResponse> OnRpcInvokeResponse
        {
            add => PlayServRpc.OnRpcInvokeResponse += value;
            remove => PlayServRpc.OnRpcInvokeResponse -= value;
        }

        public static void Send<T>(T command) =>
            PlayServRpc.Send(command);

        public static void Send<T>(T command, string moduleName) =>
            PlayServRpc.Send(command, moduleName);

#if !PLAYSERV_MODULE_DISABLED_LOCAL_EXECUTION_CORE && !PLAYSERV_MODULE_DISABLED_LOCAL_EXECUTION_SERVER
        public static void SetCommandHandler(ICommandHandler commandHandler) =>
            PlayServRpc.SetCommandHandler(commandHandler);
#endif

        public static void Invoke(string serviceName, string methodName, object payload) =>
            PlayServRpc.Invoke(serviceName, methodName, payload);

        public static void InvokeArgs(string serviceName, string methodName, params object[] args) =>
            PlayServRpc.InvokeArgs(serviceName, methodName, args);

        public static void InvokeNamed(string serviceName, string methodName, IDictionary<string, object> payload) =>
            PlayServRpc.InvokeNamed(serviceName, methodName, payload);

        public static void Invoke(string serviceName, string methodName, string payloadBase64) =>
            PlayServRpc.Invoke(serviceName, methodName, payloadBase64);

        public static void Invoke<TService>(Expression<Action<TService>> method) =>
            PlayServRpc.Invoke(method);

        public static void Invoke<TService>(Expression<Action<TService>> method, object payload) =>
            PlayServRpc.Invoke(method, payload);

        public static void Invoke<TService>(Expression<Action<TService>> method, string payloadBase64) =>
            PlayServRpc.Invoke(method, payloadBase64);
    }
}
#endif
