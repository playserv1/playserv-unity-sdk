using System;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Modules;
using Playserv.Proxy.Interfaces;
using Playserv.Runtime.Abstractions;
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
            int keepAlivePongTimeoutMs,
            IPlayServRuntimeTokenProvider runtimeTokenProvider = null,
            string playerAccessToken = null);

        /// <summary>
        /// Pushes a rotated player access token into the live connection without reconnecting.
        /// </summary>
        Task<bool> RefreshPlayerAuthAsync(
            string newAccessToken,
            CancellationToken cancellationToken = default);

        ITransportImplementation GetTransportImplementation();
    }
}
