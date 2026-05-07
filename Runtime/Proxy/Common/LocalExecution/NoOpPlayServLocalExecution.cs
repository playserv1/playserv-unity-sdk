using System;

namespace Playserv.Proxy.Common
{
    public sealed class NoOpPlayServLocalExecution :
        ILocalCommandExecution,
        ILocalEventExecution,
        ILocalRpcExecution
    {
        public bool TryHandleCommand(object command, string moduleName, bool hasTransport) => false;

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

        public bool TryInvokeRpc(string serviceName, string methodName, string payloadBase64, bool hasTransport) => false;
    }
}
