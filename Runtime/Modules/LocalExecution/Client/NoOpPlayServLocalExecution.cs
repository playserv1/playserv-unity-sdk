using System;

namespace Playserv.Server
{
    public sealed class NoOpPlayServLocalExecution : IPlayServLocalExecution
    {
#if !PLAYSERV_MODULE_DISABLED_LOCAL_EXECUTION_SERVER
        public void SetCommandHandler(ICommandHandler commandHandler)
        {
            ThrowIfProvided(commandHandler);
        }
#endif

        public bool TryHandleCommand(object command, string moduleName, bool hasTransport) => false;

#if !PLAYSERV_MODULE_DISABLED_EVENTS
#if !PLAYSERV_MODULE_DISABLED_LOCAL_EXECUTION_SERVER
        public void SetEventHandler(IEventHandler eventHandler)
        {
            ThrowIfProvided(eventHandler);
        }
#endif

        public bool TrySubscribe<T>(bool hasTransport, out IObservable<T> observable)
        {
            observable = null;
            return false;
        }

        public bool TrySubscribe<T>(Action<T> onNext, bool hasTransport, out IDisposable subscription)
        {
            subscription = null;
            return false;
        }

        public bool TryPublish<T>(T @event, bool hasTransport) => false;

        public bool TryPublishForGroup<T>(string groupName, T @event, bool hasTransport) => false;

        public bool TryPublishForUser<T>(string userId, T @event, bool hasTransport) => false;
#endif

#if !PLAYSERV_MODULE_DISABLED_RPC_CORE && !PLAYSERV_MODULE_DISABLED_SERVER_RPC && !PLAYSERV_MODULE_DISABLED_LOCAL_EXECUTION_SERVER
        public void SetRpcInvoker(object rpcInvoker)
        {
            ThrowIfProvided(rpcInvoker);
        }
#endif

#if !PLAYSERV_MODULE_DISABLED_RPC_CORE && !PLAYSERV_MODULE_DISABLED_CLIENT_RPC
        public bool TryInvokeRpc(string serviceName, string methodName, string payloadBase64, bool hasTransport) => false;
#endif

#if !PLAYSERV_MODULE_DISABLED_LOCAL_EXECUTION_SERVER
        private static void ThrowIfProvided(object value)
        {
            if (value == null)
                return;

            throw new InvalidOperationException(
                "Server local execution module is not installed or enabled.");
        }
#endif
    }
}
