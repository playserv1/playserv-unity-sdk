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

        event Action<PlayServError> OnError;

        event Action OnKeepAlivePingSent;

        event Action OnKeepAlivePongReceived;

        void Config(PlayServSettings settings);

        void Config(
            string clientToken,
            string gameId,
            string userId,
            string gameVersion,
            string sdkVersion = null);

        void SetRuntimeTokenProvider(IPlayServRuntimeTokenProvider tokenProvider);

        Task<bool> Connect();

        /// <summary>
        /// Pushes a rotated player access token into the live connection without reconnecting.
        /// </summary>
        Task<bool> RefreshPlayerAuthAsync(
            string newAccessToken,
            CancellationToken cancellationToken = default);

        void SetWebRtcSignalingClientFactory(Func<PlayServRuntimeSettings, IWebRtcSignalingClient> signalingClientFactory);

        Task<string> GetLatestVersionAsync(string gameId, CancellationToken ct = default);

        Proxy.Interfaces.ITransportImplementation GetTransportImplementation();

        void Disconnect();
    }
}
