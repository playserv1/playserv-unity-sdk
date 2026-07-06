using System;
using System.Threading.Tasks;
using Playserv.Modules;
using Playserv.Proxy.Interfaces;
using Playserv.Wrapper;

namespace Playserv.Proxy.Common
{
    public interface IPlayServRuntimeSession : IDisposable, IPlayServCommandBus, IPlayServModuleServiceHost
    {
        event Action<TransportError> OnTransportError;

        event Action OnKeepAlivePingSent;

        event Action OnKeepAlivePongReceived;

        event Action<string, object> OnModuleCommand;

        Task<bool> Connect();

        void Send<T>(T command, string moduleName = null);

        void SetConfig(
            string gameAccessToken,
            string gameId,
            string userId,
            string gameVersion,
            string sdkVersion,
            bool allowMultipleConnections,
            int keepAlivePingIntervalMs,
            int keepAlivePongTimeoutMs);

        ITransportImplementation GetTransportImplementation();
    }
}
