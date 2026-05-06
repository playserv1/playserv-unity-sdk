using System;
#if !PLAYSERV_MODULE_DISABLED_RPC_CORE && !PLAYSERV_MODULE_DISABLED_SERVER_RPC
using Playserv.RPC;

namespace Playserv.Server
{
    public sealed class PlayServServerLocalExecution : IPlayServLocalExecution
    {
        private ICommandHandler _commandHandler;
#if !PLAYSERV_MODULE_DISABLED_EVENTS
        private IEventHandler _eventHandler;
#endif
#if !PLAYSERV_MODULE_DISABLED_RPC_CORE && !PLAYSERV_MODULE_DISABLED_SERVER_RPC
        private IRpcInvoker _rpcInvoker;
#endif

        public void SetCommandHandler(ICommandHandler commandHandler) => _commandHandler = commandHandler;
#if !PLAYSERV_MODULE_DISABLED_EVENTS
        public void SetEventHandler(IEventHandler eventHandler) => _eventHandler = eventHandler;
#endif
#if !PLAYSERV_MODULE_DISABLED_RPC_CORE && !PLAYSERV_MODULE_DISABLED_SERVER_RPC
        public void SetRpcInvoker(object rpcInvoker) => _rpcInvoker = rpcInvoker as IRpcInvoker;
#endif

        public bool TryHandleCommand(object command, string moduleName, bool hasTransport)
        {
            if (_commandHandler == null)
                return false;

            if (_commandHandler.TryHandle(command, moduleName))
                return true;

            if (!hasTransport)
            {
                throw new InvalidOperationException(
                    $"Local command handler did not handle module '{moduleName ?? "<default>"}'. " +
                    "Register handler for module or connect transport.");
            }

            return false;
        }

#if !PLAYSERV_MODULE_DISABLED_EVENTS
        public bool TrySubscribe<T>(bool hasTransport, out IObservable<T> observable)
        {
            if (_eventHandler != null &&
                _eventHandler.TrySubscribe<T>(out var localObservable) &&
                localObservable != null)
            {
                observable = localObservable;
                return true;
            }

            if (_eventHandler != null && !hasTransport)
            {
                throw new InvalidOperationException(
                    $"Local event handler did not provide observable subscription for '{typeof(T).Name}'. " +
                    "Provide event handler subscription or connect transport.");
            }

            observable = null;
            return false;
        }

        public bool TrySubscribe<T>(Action<T> onNext, bool hasTransport, out IDisposable subscription)
        {
            if (_eventHandler != null &&
                _eventHandler.TrySubscribe(onNext, out var localSubscription) &&
                localSubscription != null)
            {
                subscription = localSubscription;
                return true;
            }

            if (_eventHandler != null && !hasTransport)
            {
                throw new InvalidOperationException(
                    $"Local event handler did not handle callback subscription for '{typeof(T).Name}'. " +
                    "Provide event handler subscription or connect transport.");
            }

            subscription = null;
            return false;
        }

        public bool TryPublish<T>(T @event, bool hasTransport)
        {
            if (_eventHandler == null)
                return false;

            if (_eventHandler.TryPublish(@event))
                return true;

            if (!hasTransport)
            {
                throw new InvalidOperationException(
                    $"Local event handler did not handle publish for '{typeof(T).Name}'. " +
                    "Provide event handler publish route or connect transport.");
            }

            return false;
        }

        public bool TryPublishForGroup<T>(string groupName, T @event, bool hasTransport)
        {
            if (_eventHandler == null)
                return false;

            if (_eventHandler.TryPublishForGroup(groupName, @event))
                return true;

            if (!hasTransport)
            {
                throw new InvalidOperationException(
                    $"Local event handler did not handle group publish '{groupName}' for '{typeof(T).Name}'. " +
                    "Provide event handler group route or connect transport.");
            }

            return false;
        }

        public bool TryPublishForUser<T>(string userId, T @event, bool hasTransport)
        {
            if (_eventHandler == null)
                return false;

            if (_eventHandler.TryPublishForUser(userId, @event))
                return true;

            if (!hasTransport)
            {
                throw new InvalidOperationException(
                    $"Local event handler did not handle user publish '{userId}' for '{typeof(T).Name}'. " +
                    "Provide event handler user route or connect transport.");
            }

            return false;
        }
#endif

#if !PLAYSERV_MODULE_DISABLED_RPC_CORE && !PLAYSERV_MODULE_DISABLED_CLIENT_RPC
        public bool TryInvokeRpc(string serviceName, string methodName, string payloadBase64, bool hasTransport)
        {
#if PLAYSERV_MODULE_DISABLED_SERVER_RPC
            return false;
#else
            if (_rpcInvoker == null)
                return false;

            if (_rpcInvoker.TryInvoke(serviceName, methodName, payloadBase64))
                return true;

            if (!hasTransport)
            {
                throw new InvalidOperationException(
                    $"Server RPC invoker did not handle '{serviceName}.{methodName}'. " +
                    "Register service in invoker or connect transport.");
            }

            return false;
#endif
        }
#endif
    }
}
#endif
