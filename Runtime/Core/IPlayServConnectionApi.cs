using System;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Proxy.Common;
using Playserv.Runtime.Abstractions;

namespace Playserv.Wrapper
{
    public interface IPlayServConnectionApi
    {
        string SdkVersion { get; }

        PlayServSettings Settings { get; }

        PlayServState State { get; }

        event Action<TransportError> OnTransportError;

        event Action OnKeepAlivePingSent;

        event Action OnKeepAlivePongReceived;

        void Config(PlayServSettings settings);

        void Config(
            string clientToken,
            string gameId,
            string userId,
            string gameVersion,
            string sdkVersion = null,
            string authorization = null);

        Task<bool> Connect();

        void SetWebRtcSignalingClientFactory(Func<PlayServRuntimeSettings, IWebRtcSignalingClient> signalingClientFactory);

        Task<string> GetLatestVersionAsync(string gameId, CancellationToken ct = default);

        Proxy.Interfaces.ITransportImplementation GetTransportImplementation();

        void Disconnect();
    }
}
