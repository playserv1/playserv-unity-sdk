using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Playserv.Proxy.Common;
using Playserv.RPC;
#if !PLAYSERV_MODULE_DISABLED_LOCAL_EXECUTION_CORE && !PLAYSERV_MODULE_DISABLED_LOCAL_EXECUTION_SERVER
using Playserv.Server;
#endif

namespace Playserv.Wrapper
{
    public interface IPlayServRpcApi
    {
        event Action<InvokeRpcResponse> OnRpcInvokeResponse;

        void Send<T>(T command);

        void Send<T>(T command, string moduleName);

#if !PLAYSERV_MODULE_DISABLED_LOCAL_EXECUTION_CORE && !PLAYSERV_MODULE_DISABLED_LOCAL_EXECUTION_SERVER
        void SetCommandHandler(ICommandHandler commandHandler);
#endif

        void Invoke(string serviceName, string methodName, object payload);

        void InvokeArgs(string serviceName, string methodName, params object[] args);

        void InvokeNamed(string serviceName, string methodName, IDictionary<string, object> payload);

        void Invoke(string serviceName, string methodName, string payloadBase64);

        void Invoke<TService>(Expression<Action<TService>> method);

        void Invoke<TService>(Expression<Action<TService>> method, object payload);

        void Invoke<TService>(Expression<Action<TService>> method, string payloadBase64);
    }
}
