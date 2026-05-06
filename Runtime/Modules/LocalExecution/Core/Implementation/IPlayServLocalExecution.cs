#if !PLAYSERV_MODULE_DISABLED_LOCAL_EXECUTION_CORE
using System;

namespace Playserv.Server
{
    public interface IPlayServLocalExecution
    {
#if !PLAYSERV_MODULE_DISABLED_LOCAL_EXECUTION_SERVER
        void SetCommandHandler(ICommandHandler commandHandler);
#endif

        bool TryHandleCommand(object command, string moduleName, bool hasTransport);

#if !PLAYSERV_MODULE_DISABLED_EVENTS
#if !PLAYSERV_MODULE_DISABLED_LOCAL_EXECUTION_SERVER
        void SetEventHandler(IEventHandler eventHandler);
#endif

        bool TrySubscribe<T>(bool hasTransport, out IObservable<T> observable);

        bool TrySubscribe<T>(Action<T> onNext, bool hasTransport, out IDisposable subscription);

        bool TryPublish<T>(T @event, bool hasTransport);

        bool TryPublishForGroup<T>(string groupName, T @event, bool hasTransport);

        bool TryPublishForUser<T>(string userId, T @event, bool hasTransport);
#endif

#if !PLAYSERV_MODULE_DISABLED_RPC_CORE && !PLAYSERV_MODULE_DISABLED_SERVER_RPC && !PLAYSERV_MODULE_DISABLED_LOCAL_EXECUTION_SERVER
        void SetRpcInvoker(object rpcInvoker);
#endif

#if !PLAYSERV_MODULE_DISABLED_RPC_CORE && !PLAYSERV_MODULE_DISABLED_CLIENT_RPC
        bool TryInvokeRpc(string serviceName, string methodName, string payloadBase64, bool hasTransport);
#endif
    }
}
#endif
