using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Playserv.DataSubscription;
using Playserv.DataSubscription.Responses;
using Playserv.Modules;
using Playserv.Proxy.Common;
using Playserv.RPC;
using Playserv.Runtime.Abstractions;
using Playserv.Server;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

namespace Playserv.Wrapper
{
    /// <summary>
    /// Compatibility and convenience facade for PlayServ SDK runtime operations.
    /// Optional module calls are routed through assembly-owned compatibility providers.
    /// </summary>
    public static class PlayServ
    {
        private static IPlayServConnectionApi ConnectionApi => PlayServApiHost.Connection;
        private static IPlayServEventsApi EventsApi =>
            PlayServLegacyApiRegistry.GetRequired<IPlayServEventsApi>();
        private static IPlayServDataApi DataApi =>
            PlayServLegacyApiRegistry.GetRequired<IPlayServDataApi>();
        private static IPlayServRpcApi RpcApi =>
            PlayServLegacyApiRegistry.GetRequired<IPlayServRpcApi>();
        private static IPlayServServerRpcApi ServerApi =>
            PlayServLegacyApiRegistry.GetRequired<IPlayServServerRpcApi>();
#if UNITY_5_3_OR_NEWER
        private static IPlayServSpawnApi SpawnApi =>
            PlayServLegacyApiRegistry.GetRequired<IPlayServSpawnApi>();
#endif

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

        public static Task<ISharedEntity<TDto>> SelectEntity<TEntity, TDto>(
            string playerId,
            Func<TEntity, TDto> map,
            DataSubscriptionMode mode = DataSubscriptionMode.Polling)
            where TEntity : class
            where TDto : class, new() =>
            DataApi.SelectEntity<TEntity, TDto>(playerId, map, mode);

        public static Task<DataGetResponse> GetDataByKeyAsync(
            string key,
            string query,
            Dictionary<string, object> variables,
            CancellationToken ct = default) =>
            DataApi.GetDataByKeyAsync(key, query, variables, ct);

        public static IDisposable StartDataByKeyPolling(
            string key,
            string query,
            Dictionary<string, object> variables,
            Action<DataGetResponse> onData,
            Action<Exception> onError = null) =>
            DataApi.StartDataByKeyPolling(key, query, variables, onData, onError);

        public static IObservable<T> Subscribe<T>() =>
            EventsApi.Subscribe<T>();

        public static IDisposable Subscribe<T>(Action<T> onNext) =>
            EventsApi.Subscribe(onNext);

        public static void Publish<T>(T @event) =>
            EventsApi.Publish(@event);

        public static void PublishForGroup<T>(string groupName, T @event) =>
            EventsApi.PublishForGroup(groupName, @event);

        public static void PublishForUser<T>(string userId, T @event) =>
            EventsApi.PublishForUser(userId, @event);

        public static Task<bool> SubscribeGroupAsync(string groupName, CancellationToken ct = default) =>
            EventsApi.SubscribeGroupAsync(groupName, ct);

        public static Task<bool> UnsubscribeGroupAsync(string groupName, CancellationToken ct = default) =>
            EventsApi.UnsubscribeGroupAsync(groupName, ct);

        public static event Action<InvokeRpcResponse> OnRpcInvokeResponse
        {
            add => RpcApi.OnRpcInvokeResponse += value;
            remove => RpcApi.OnRpcInvokeResponse -= value;
        }

        public static void Send<T>(T command) =>
            RpcApi.Send(command);

        public static void Send<T>(T command, string moduleName) =>
            RpcApi.Send(command, moduleName);

        public static void Invoke(string serviceName, string methodName, object payload) =>
            RpcApi.Invoke(serviceName, methodName, payload);

        public static void InvokeArgs(string serviceName, string methodName, params object[] args) =>
            RpcApi.InvokeArgs(serviceName, methodName, args);

        public static void InvokeNamed(string serviceName, string methodName, IDictionary<string, object> payload) =>
            RpcApi.InvokeNamed(serviceName, methodName, payload);

        public static void Invoke(string serviceName, string methodName, string payloadBase64) =>
            RpcApi.Invoke(serviceName, methodName, payloadBase64);

        public static void Invoke(string serviceName, string methodName, object payload, string coalesceKey) =>
            RpcApi.Invoke(serviceName, methodName, payload, coalesceKey);

        public static void Invoke(
            string serviceName,
            string methodName,
            object payload,
            string coalesceKey,
            bool fireAndForget) =>
            RpcApi.Invoke(serviceName, methodName, payload, coalesceKey, fireAndForget);

        public static void Invoke<TService>(Expression<Action<TService>> method) =>
            RpcApi.Invoke(method);

        public static void Invoke<TService>(Expression<Action<TService>> method, object payload) =>
            RpcApi.Invoke(method, payload);

        public static void Invoke<TService>(Expression<Action<TService>> method, string payloadBase64) =>
            RpcApi.Invoke(method, payloadBase64);

        public static void SetCommandHandler(ICommandHandler commandHandler) =>
            ServerApi.SetCommandHandler(commandHandler);

        public static void SetEventHandler(IEventHandler eventHandler) =>
            ServerApi.SetEventHandler(eventHandler);

        public static void SetRpcInvoker(IRpcInvoker rpcInvoker) =>
            ServerApi.SetRpcInvoker(rpcInvoker);

#if UNITY_5_3_OR_NEWER
        public static Task<GameObject> Spawn(string assetName, Vector3 position, Quaternion rotation) =>
            SpawnApi.Spawn(assetName, position, rotation);

        public static Task<GameObject> Spawn(string assetName, Vector3 position) =>
            SpawnApi.Spawn(assetName, position);

        public static string CurrentSpawnScope => SpawnApi.CurrentSpawnScope;

        public static Task<bool> JoinSpawnScopeAsync(string groupName, CancellationToken ct = default) =>
            SpawnApi.JoinSpawnScopeAsync(groupName, ct);

        public static Task<bool> JoinSpawnScope(string groupName, CancellationToken ct = default) =>
            SpawnApi.JoinSpawnScopeAsync(groupName, ct);

        public static Task<bool> LeaveSpawnScopeAsync(CancellationToken ct = default) =>
            SpawnApi.LeaveSpawnScopeAsync(ct);

        public static Task<bool> LeaveSpawnScope(CancellationToken ct = default) =>
            SpawnApi.LeaveSpawnScopeAsync(ct);

        public static bool Despawn(string spawnId) =>
            SpawnApi.Despawn(spawnId);

        public static bool Despawn(GameObject instance) =>
            SpawnApi.Despawn(instance);
#endif
    }
}
