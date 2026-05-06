using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
#if !PLAYSERV_DISABLE_DATA
using Playserv.DataSubscription;
using Playserv.DataSubscription.Responses;
#endif
using Playserv.Proxy.Common;
#if !PLAYSERV_DISABLE_RPC_CORE && (!PLAYSERV_DISABLE_CLIENT_RPC || !PLAYSERV_DISABLE_SERVER_RPC)
using Playserv.RPC;
#endif
using Playserv.Runtime.Abstractions;
#if !PLAYSERV_DISABLE_LOCAL_EXECUTION_CORE && !PLAYSERV_DISABLE_LOCAL_EXECUTION_SERVER
using Playserv.Server;
#endif
#if UNITY_5_3_OR_NEWER && !PLAYSERV_DISABLE_SPAWN && !PLAYSERV_DISABLE_EVENTS
using Playserv.Spawn;
using UnityEngine;
#endif

namespace Playserv.Wrapper
{
    /// <summary>
    /// Compatibility and convenience facade for PlayServ SDK runtime operations.
    /// Domain-specific entrypoints such as PlayServEvents, PlayServData, PlayServRpc and PlayServSpawn
    /// are additive; existing PlayServ.* calls remain supported when their modules are enabled.
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

#if !PLAYSERV_DISABLE_RPC_CORE && !PLAYSERV_DISABLE_CLIENT_RPC
        /// <summary>
        /// Raised when RPC module returns InvokeRpcResponse command.
        /// </summary>
        public static event Action<InvokeRpcResponse> OnRpcInvokeResponse
        {
            add => PlayServRpc.OnRpcInvokeResponse += value;
            remove => PlayServRpc.OnRpcInvokeResponse -= value;
        }
#endif

        /// <summary>
        /// Applies full SDK settings object.
        /// </summary>
        public static void Config(PlayServSettings settings) =>
            ConnectionApi.Config(settings);

        /// <summary>
        /// Applies basic SDK connection settings.
        /// </summary>
        public static void Config(string gameAccessToken, string gameId, string userId, string gameVersion, string sdkVersion = null) =>
            ConnectionApi.Config(gameAccessToken, gameId, userId, gameVersion, sdkVersion);

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

#if !PLAYSERV_DISABLE_EVENTS
        public static IObservable<T> Subscribe<T>() =>
            PlayServEvents.Subscribe<T>();

        public static IDisposable Subscribe<T>(Action<T> onNext) =>
            PlayServEvents.Subscribe(onNext);

        public static void Publish<T>(T @event) =>
            PlayServEvents.Publish(@event);

        public static void PublishForGroup<T>(string groupName, T @event) =>
            PlayServEvents.PublishForGroup(groupName, @event);

        public static void PublishForUser<T>(string userId, T @event) =>
            PlayServEvents.PublishForUser(userId, @event);

        public static Task<bool> SubscribeGroupAsync(string groupName, CancellationToken ct = default) =>
            PlayServEvents.SubscribeGroupAsync(groupName, ct);

        public static Task<bool> UnsubscribeGroupAsync(string groupName, CancellationToken ct = default) =>
            PlayServEvents.UnsubscribeGroupAsync(groupName, ct);

#if !PLAYSERV_DISABLE_LOCAL_EXECUTION_CORE && !PLAYSERV_DISABLE_LOCAL_EXECUTION_SERVER
        public static void SetEventHandler(IEventHandler eventHandler) =>
            PlayServEvents.SetEventHandler(eventHandler);
#endif
#endif

#if !PLAYSERV_DISABLE_RPC_CORE && !PLAYSERV_DISABLE_CLIENT_RPC
        public static void Send<T>(T command) =>
            PlayServRpc.Send(command);

        public static void Send<T>(T command, string moduleName) =>
            PlayServRpc.Send(command, moduleName);

#if !PLAYSERV_DISABLE_LOCAL_EXECUTION_CORE && !PLAYSERV_DISABLE_LOCAL_EXECUTION_SERVER
        public static void SetCommandHandler(ICommandHandler commandHandler) =>
            PlayServRpc.SetCommandHandler(commandHandler);
#endif

        public static void Invoke(string serviceName, string methodName, object payload) =>
            PlayServRpc.Invoke(serviceName, methodName, payload);

        public static void InvokeArgs(string serviceName, string methodName, params object[] args) =>
            PlayServRpc.InvokeArgs(serviceName, methodName, args);

        public static void InvokeNamed(string serviceName, string methodName, IDictionary<string, object> payload) =>
            PlayServRpc.InvokeNamed(serviceName, methodName, payload);

        public static void Invoke(string serviceName, string methodName, string payloadBase64) =>
            PlayServRpc.Invoke(serviceName, methodName, payloadBase64);

        public static void Invoke<TService>(Expression<Action<TService>> method) =>
            PlayServRpc.Invoke(method);

        public static void Invoke<TService>(Expression<Action<TService>> method, object payload) =>
            PlayServRpc.Invoke(method, payload);

        public static void Invoke<TService>(Expression<Action<TService>> method, string payloadBase64) =>
            PlayServRpc.Invoke(method, payloadBase64);
#endif

#if !PLAYSERV_DISABLE_LOCAL_EXECUTION_CORE && !PLAYSERV_DISABLE_RPC_CORE && !PLAYSERV_DISABLE_SERVER_RPC && !PLAYSERV_DISABLE_LOCAL_EXECUTION_SERVER
        public static void SetRpcInvoker(IRpcInvoker rpcInvoker) =>
            PlayServServerRpc.SetRpcInvoker(rpcInvoker);
#endif

        /// <summary>
        /// Returns low-level transport implementation used by SDK.
        /// Intended for testing and protocol diagnostics only.
        /// </summary>
        public static Playserv.Proxy.Interfaces.ITransportImplementation GetTransportImplementation() =>
            ConnectionApi.GetTransportImplementation();

#if UNITY_5_3_OR_NEWER && !PLAYSERV_DISABLE_SPAWN && !PLAYSERV_DISABLE_EVENTS
        public static Task<GameObject> Spawn(string assetName, Vector3 position, Quaternion rotation) =>
            PlayServSpawn.Spawn(assetName, position, rotation);

        public static Task<GameObject> Spawn(string assetName, Vector3 position) =>
            PlayServSpawn.Spawn(assetName, position);

        public static string CurrentSpawnScope => PlayServSpawn.CurrentScope;

        public static Task<bool> JoinSpawnScopeAsync(string groupName, CancellationToken ct = default) =>
            PlayServSpawn.JoinSpawnScopeAsync(groupName, ct);

        public static Task<bool> JoinSpawnScope(string groupName, CancellationToken ct = default) =>
            PlayServSpawn.JoinSpawnScope(groupName, ct);

        public static Task<bool> LeaveSpawnScopeAsync(CancellationToken ct = default) =>
            PlayServSpawn.LeaveSpawnScopeAsync(ct);

        public static Task<bool> LeaveSpawnScope(CancellationToken ct = default) =>
            PlayServSpawn.LeaveSpawnScope(ct);

        public static bool Despawn(string spawnId) =>
            PlayServSpawn.Despawn(spawnId);

        public static bool Despawn(GameObject instance) =>
            PlayServSpawn.Despawn(instance);

        public static void SetSpawnPrefabRegistry(INetworkPrefabRegistry prefabRegistry) =>
            PlayServSpawn.SetPrefabRegistry(prefabRegistry);
#endif

#if !PLAYSERV_DISABLE_DATA
        public static Task<ISharedEntity<TDto>> SelectEntity<TEntity, TDto>(
            string playerId,
            Func<TEntity, TDto> map,
            DataSubscriptionMode mode = DataSubscriptionMode.Polling)
            where TEntity : class
            where TDto : class, new() =>
            PlayServData.SelectEntity<TEntity, TDto>(playerId, map, mode);

        public static Task<DataGetResponse> GetDataByKeyAsync(
            string key,
            string query,
            Dictionary<string, object> variables,
            CancellationToken ct = default) =>
            PlayServData.GetDataByKeyAsync(key, query, variables, ct);

        public static IDisposable StartDataByKeyPolling(
            string key,
            string query,
            Dictionary<string, object> variables,
            Action<DataGetResponse> onData,
            Action<Exception> onError = null) =>
            PlayServData.StartDataByKeyPolling(key, query, variables, onData, onError);
#endif
    }
}
