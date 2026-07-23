using System;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Proxy.Common;
using Playserv.Runtime.Abstractions;

namespace Playserv.Wrapper
{
    /// <summary>
    /// Core PlayServ SDK connection and runtime entry point.
    /// Optional features are exposed by their module-specific APIs.
    /// </summary>
    public static class PlayServ
    {
        private static IPlayServConnectionApi ConnectionApi => PlayServApiHost.Connection;

        /// <summary>
        /// Gets current SDK version string reported by the client.
        /// </summary>
        public static string SdkVersion => ConnectionApi.SdkVersion;

        /// <summary>
        /// Get SDK settings.
        /// </summary>
        public static PlayServSettings Settings => ConnectionApi.Settings;

        /// <summary>
        /// Gets current connection state of the SDK transport.
        /// </summary>
        public static PlayServState State => ConnectionApi.State;

        /// <summary>
        /// Raised when transport-level error happens.
        /// </summary>
        public static event Action<TransportError> OnTransportError
        {
            add => ConnectionApi.OnTransportError += value;
            remove => ConnectionApi.OnTransportError -= value;
        }

        /// <summary>
        /// Raised every time keepalive ping is sent by the client.
        /// </summary>
        public static event Action OnKeepAlivePingSent
        {
            add => ConnectionApi.OnKeepAlivePingSent += value;
            remove => ConnectionApi.OnKeepAlivePingSent -= value;
        }

        /// <summary>
        /// Raised when keepalive pong is received from server.
        /// </summary>
        public static event Action OnKeepAlivePongReceived
        {
            add => ConnectionApi.OnKeepAlivePongReceived += value;
            remove => ConnectionApi.OnKeepAlivePongReceived -= value;
        }

        /// <summary>
        /// Applies full SDK settings object.
        /// </summary>
        public static void Config(PlayServSettings settings) =>
            ConnectionApi.Config(settings);

        /// <summary>
        /// Applies basic SDK connection settings.
        /// </summary>
        public static void Config(
            string gameAccessToken,
            string gameId,
            string userId,
            string gameVersion,
            string sdkVersion = null,
            string authorization = null) =>
            ConnectionApi.Config(gameAccessToken, gameId, userId, gameVersion, sdkVersion, authorization);

        /// <summary>
        /// Connects to configured PlayServ endpoint and performs handshake.
        /// </summary>
        public static Task<bool> Connect() =>
            ConnectionApi.Connect();

        /// <summary>
        /// Disconnects SDK transport and disposes internal runtime instance.
        /// </summary>
        public static void Disconnect() =>
            ConnectionApi.Disconnect();

        /// <summary>
        /// Registers application-provided signaling client factory for WebRTC DataChannel transport.
        /// </summary>
        public static void SetWebRtcSignalingClientFactory(Func<PlayServRuntimeSettings, IWebRtcSignalingClient> signalingClientFactory) =>
            ConnectionApi.SetWebRtcSignalingClientFactory(signalingClientFactory);

        /// <summary>
        /// Requests latest deployed game version from deployment API by game identifier.
        /// </summary>
        public static Task<string> GetLatestVersionAsync(string gameId, CancellationToken ct = default) =>
            ConnectionApi.GetLatestVersionAsync(gameId, ct);

        /// <summary>
        /// Returns low-level transport implementation used by SDK.
        /// Intended for testing and protocol diagnostics only.
        /// </summary>
        public static Playserv.Proxy.Interfaces.ITransportImplementation GetTransportImplementation() =>
            ConnectionApi.GetTransportImplementation();
    }
}
