using System;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Proxy.Common;
using Playserv.Runtime.Abstractions;

namespace Playserv.Wrapper
{
    /// <summary>
    /// Connection-oriented PlayServ SDK surface.
    /// </summary>
    public static class PlayServConnection
    {
        private static IPlayServConnectionApi Api => PlayServApiHost.Connection;

        public static string SdkVersion => Api.SdkVersion;

        public static PlayServSettings Settings => Api.Settings;

        public static PlayServState State => Api.State;

        public static event Action<TransportError> OnTransportError
        {
            add => Api.OnTransportError += value;
            remove => Api.OnTransportError -= value;
        }

        public static event Action OnKeepAlivePingSent
        {
            add => Api.OnKeepAlivePingSent += value;
            remove => Api.OnKeepAlivePingSent -= value;
        }

        public static event Action OnKeepAlivePongReceived
        {
            add => Api.OnKeepAlivePongReceived += value;
            remove => Api.OnKeepAlivePongReceived -= value;
        }

        public static void Config(PlayServSettings settings) => Api.Config(settings);

        public static void Config(
            string clientToken,
            string gameId,
            string userId,
            string gameVersion,
            string sdkVersion = null) =>
            Api.Config(clientToken, gameId, userId, gameVersion, sdkVersion);

        public static void SetRuntimeTokenProvider(IPlayServRuntimeTokenProvider tokenProvider) =>
            Api.SetRuntimeTokenProvider(tokenProvider);

        public static Task<bool> Connect() => Api.Connect();

        public static Task<bool> RefreshPlayerAuthAsync(
            string newAccessToken,
            CancellationToken cancellationToken = default) =>
            Api.RefreshPlayerAuthAsync(newAccessToken, cancellationToken);

        public static void Disconnect() => Api.Disconnect();

        public static void SetWebRtcSignalingClientFactory(Func<PlayServRuntimeSettings, IWebRtcSignalingClient> signalingClientFactory) =>
            Api.SetWebRtcSignalingClientFactory(signalingClientFactory);

        public static Task<string> GetLatestVersionAsync(string gameId, CancellationToken ct = default) =>
            Api.GetLatestVersionAsync(gameId, ct);

        public static Playserv.Proxy.Interfaces.ITransportImplementation GetTransportImplementation() =>
            Api.GetTransportImplementation();
    }
}
