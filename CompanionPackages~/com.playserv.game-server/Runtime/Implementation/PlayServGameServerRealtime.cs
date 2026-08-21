using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Data;
using Playserv.DataSubscription;
using Playserv.Modules;
using Playserv.Proxy.Common;
using Playserv.Runtime.Abstractions;
using Playserv.Wrapper;
using UnityEngine;

namespace Playserv.GameServer
{
    /// <summary>
    /// Dedicated-server WebSocket session used by typed Records subscriptions. It is independent
    /// from the player SDK connection and resolves the rotating server key on every reconnect.
    /// </summary>
    public sealed class PlayServGameServerRealtime
    {
        private static readonly string ProcessInstanceId =
            "unity-server-" + Guid.NewGuid().ToString("N");

        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private PlayServImplementation _session;
        private PlayServApiDataFacade _data;
        private PlayServTransportImplementationFactory _transportFactoryForTesting;

        internal PlayServGameServerRealtime()
        {
        }

        /// <summary>Current state of the independent server WebSocket session.</summary>
        public PlayServState State => _session?.State ?? PlayServState.Offline;

        /// <summary>Raised for normalized handshake, transport and reconnect failures.</summary>
        public event Action<PlayServError> OnError;

        /// <summary>Connects the opt-in server realtime session.</summary>
        public async Task<bool> ConnectAsync(
            PlayServGameServerRealtimeOptions options = null,
            CancellationToken cancellationToken = default)
        {
            PlayServGameServer.EnsureSupportedBuildForRealtime();
            cancellationToken.ThrowIfCancellationRequested();
            options = options ?? new PlayServGameServerRealtimeOptions();
            var normalized = NormalizeOptions(options);
            var context = PlayServGameServer.GetContextForRealtime();

            await _gate.WaitAsync(cancellationToken);
            try
            {
                if (_session != null && _session.State == PlayServState.Online)
                    return true;

                DisposeSession();
                var endpoint = BuildEndpoint(context.BackendServerAddress);
                Action<PlayServModuleHost> registerModules =
                    host => host.Register(new PlayServDataSubscriptionModule());
                var session = _transportFactoryForTesting == null
                    ? new PlayServImplementation(endpoint, registerModules)
                    : new PlayServImplementation(endpoint, _transportFactoryForTesting, registerModules);
                session.OnTransportError += HandleTransportError;
                session.SetServerConfig(
                    ct => PlayServGameServer.ResolveServerKeyForRealtimeAsync(context, ct),
                    normalized.GameId,
                    normalized.InstanceId,
                    normalized.GameVersion,
                    SdkInfo.Version,
                    ToMilliseconds(normalized.KeepAliveInterval, nameof(options.KeepAliveInterval)),
                    ToMilliseconds(normalized.KeepAliveTimeout, nameof(options.KeepAliveTimeout)));

                _session = session;
                _data = new PlayServApiDataFacade(new SessionDataRuntimeAccess(session));
                var connectTask = session.Connect();
                bool connected;
                try
                {
                    connected = await AwaitWithCancellationAsync(connectTask, cancellationToken);
                }
                catch
                {
                    if (ReferenceEquals(_session, session))
                        DisposeSession();
                    throw;
                }

                if (!connected && ReferenceEquals(_session, session))
                    DisposeSession();
                return connected;
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>Disconnects the server realtime session and terminates its active handles.</summary>
        public async Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _gate.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                DisposeSession();
            }
            finally
            {
                _gate.Release();
            }
        }

        internal Task<ISharedCollection<T>> SubscribeCollectionAsync<T>(
            string query,
            Dictionary<string, object> variables,
            CancellationToken cancellationToken) =>
            GetDataFacade().SelectTypedCollection<T>(query, variables, cancellationToken);

        internal Task<IPlayServRecordSubscription<T>> SubscribeRecordAsync<T>(
            PlayServRecord<T> record,
            string query,
            Dictionary<string, object> variables,
            string rootFieldName,
            CancellationToken cancellationToken) =>
            GetDataFacade().SelectTypedRecord(
                record,
                query,
                variables,
                rootFieldName,
                cancellationToken);

        internal void CancelForModuleShutdown()
        {
            DisposeSession();
        }

        internal void SetTransportFactoryForTesting(
            PlayServTransportImplementationFactory transportFactory)
        {
            DisposeSession();
            _transportFactoryForTesting = transportFactory;
        }

        private PlayServApiDataFacade GetDataFacade()
        {
            var data = _data;
            if (data == null || _session == null || _session.State != PlayServState.Online)
            {
                throw new Playserv.DataSubscription.Exceptions.DataSubscriptionException(
                    0,
                    "The game server realtime connection is unavailable. Call PlayServGameServer.Realtime.ConnectAsync first.",
                    retryable: true,
                    sourceCode: "subscription_connection_unavailable");
            }

            return data;
        }

        private void HandleTransportError(TransportError error)
        {
            if (error?.UnifiedError != null)
                OnError?.Invoke(error.UnifiedError);
        }

        private void DisposeSession()
        {
            var session = Interlocked.Exchange(ref _session, null);
            _data = null;
            if (session == null)
                return;
            session.OnTransportError -= HandleTransportError;
            session.Dispose();
        }

        private static PlayServGameServerRealtimeOptions NormalizeOptions(
            PlayServGameServerRealtimeOptions source)
        {
            if (source.KeepAliveInterval <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(source.KeepAliveInterval));
            if (source.KeepAliveTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(source.KeepAliveTimeout));

            return new PlayServGameServerRealtimeOptions
            {
                GameId = NormalizeMetadata(source.GameId, Application.identifier, "playserv-unity-server", nameof(source.GameId)),
                InstanceId = NormalizeMetadata(source.InstanceId, ProcessInstanceId, ProcessInstanceId, nameof(source.InstanceId)),
                GameVersion = NormalizeMetadata(source.GameVersion, Application.version, SdkInfo.Version, nameof(source.GameVersion)),
                KeepAliveInterval = source.KeepAliveInterval,
                KeepAliveTimeout = source.KeepAliveTimeout
            };
        }

        private static string NormalizeMetadata(
            string value,
            string fallback,
            string finalFallback,
            string parameterName)
        {
            var normalized = string.IsNullOrWhiteSpace(value)
                ? string.IsNullOrWhiteSpace(fallback) ? finalFallback : fallback.Trim()
                : value.Trim();
            if (normalized.IndexOf('\r') >= 0 || normalized.IndexOf('\n') >= 0)
                throw new ArgumentException("Handshake metadata cannot contain line breaks.", parameterName);
            if (normalized.Length > 256)
                throw new ArgumentOutOfRangeException(parameterName, "Handshake metadata must be at most 256 characters.");
            return normalized;
        }

        private static string BuildEndpoint(string backendAddress)
        {
            var source = new Uri(backendAddress, UriKind.Absolute);
            var builder = new UriBuilder(source)
            {
                Scheme = string.Equals(source.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                    ? "wss"
                    : "ws",
                Path = "/ws",
                Query = string.Empty,
                Fragment = string.Empty
            };
            return builder.Uri.ToString();
        }

        private static int ToMilliseconds(TimeSpan value, string parameterName)
        {
            if (value.TotalMilliseconds > int.MaxValue)
                throw new ArgumentOutOfRangeException(parameterName);
            return Math.Max(1, (int)Math.Ceiling(value.TotalMilliseconds));
        }

        private static async Task<T> AwaitWithCancellationAsync<T>(
            Task<T> operation,
            CancellationToken cancellationToken)
        {
            if (!cancellationToken.CanBeCanceled)
                return await operation;

            var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = cancellationToken.Register(() => canceled.TrySetResult(true));
            if (await Task.WhenAny(operation, canceled.Task) != operation)
                throw new OperationCanceledException(cancellationToken);
            return await operation;
        }

        private sealed class SessionDataRuntimeAccess : IPlayServDataRuntimeAccess
        {
            private readonly PlayServImplementation _session;

            internal SessionDataRuntimeAccess(PlayServImplementation session)
            {
                _session = session ?? throw new ArgumentNullException(nameof(session));
            }

            public IPlayServModuleServiceProvider RequiredServices => _session.ModuleServices;
        }
    }
}
