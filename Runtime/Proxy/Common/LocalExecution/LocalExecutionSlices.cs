using System;

namespace Playserv.Proxy.Common
{
    public interface ILocalCommandExecution
    {
        bool TryHandleCommand(object command, string moduleName, bool hasTransport);
    }

    public interface ILocalEventExecution
    {
        bool TrySubscribe<T>(bool hasTransport, out IObservable<T> observable);

        bool TrySubscribe<T>(Action<T> onNext, bool hasTransport, out IDisposable subscription);

        bool TryPublish<T>(T @event, bool hasTransport);

        bool TryPublishForGroup<T>(string groupName, T @event, bool hasTransport);

        bool TryPublishForUser<T>(string userId, T @event, bool hasTransport);
    }

    public interface ILocalRpcExecution
    {
        bool TryInvokeRpc(string serviceName, string methodName, string payloadBase64, bool hasTransport);
    }
}
