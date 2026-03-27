using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Playserv.DataSubscription;
using Playserv.DataSubscription.Responses;
using Playserv.Proxy.Common;
using Playserv.RPC;
using Playserv.Server;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

namespace Playserv.Wrapper
{
    /// <summary>
    /// Main static facade for PlayServ SDK runtime operations.
    /// </summary>
    public static class PlayServ
    {
        private static readonly PlayServApi Api = new PlayServApi();

        /// <summary>
        /// Gets current SDK version string reported by the client.
        /// </summary>
        public static string SdkVersion => Api.SdkVersion;
        
        /// <summary>
        /// Get SDK settings
        /// </summary>
        public static PlayServSettings Settings => Api.Settings;

        /// <summary>
        /// Gets current connection state of the SDK transport.
        /// </summary>
        public static PlayServState State => Api.State;

        /// <summary>
        /// Raised when transport-level error happens (handshake, connection policy, protocol, etc.).
        /// </summary>
        public static event Action<TransportError> OnTransportError
        {
            add => Api.OnTransportError += value;
            remove => Api.OnTransportError -= value;
        }

        /// <summary>
        /// Raised every time keepalive ping is sent by the client.
        /// </summary>
        public static event Action OnKeepAlivePingSent
        {
            add => Api.OnKeepAlivePingSent += value;
            remove => Api.OnKeepAlivePingSent -= value;
        }

        /// <summary>
        /// Raised when keepalive pong is received from server.
        /// </summary>
        public static event Action OnKeepAlivePongReceived
        {
            add => Api.OnKeepAlivePongReceived += value;
            remove => Api.OnKeepAlivePongReceived -= value;
        }

        /// <summary>
        /// Raised when RPC module returns InvokeRpcResponse command.
        /// </summary>
        public static event Action<InvokeRpcResponse> OnRpcInvokeResponse
        {
            add => Api.OnRpcInvokeResponse += value;
            remove => Api.OnRpcInvokeResponse -= value;
        }

        /// <summary>
        /// Applies full SDK settings object.
        /// </summary>
        /// <param name="settings">Runtime settings used to configure endpoint, auth and timeouts.</param>
        public static void Config(PlayServSettings settings) =>
            Api.Config(settings);

        /// <summary>
        /// Applies basic SDK connection settings.
        /// </summary>
        /// <param name="gameAccessToken">Access token used for handshake authorization.</param>
        /// <param name="gameId">Game identifier.</param>
        /// <param name="userId">Current player/user identifier.</param>
        /// <param name="gameVersion">Current game client version.</param>
        /// <param name="sdkVersion">Optional SDK version override. If null, default SDK version is used.</param>
        public static void Config(string gameAccessToken, string gameId, string userId, string gameVersion, string sdkVersion = null) =>
        Api.Config(gameAccessToken, gameId, userId, gameVersion, sdkVersion);

        /// <summary>
        /// Connects to configured PlayServ endpoint and performs handshake.
        /// </summary>
        /// <returns>True if connection and handshake succeeded; otherwise false.</returns>
        public static Task<bool> Connect() =>
            Api.Connect();

        /// <summary>
        /// Requests latest deployed game version from deployment API by game identifier.
        /// </summary>
        /// <param name="gameId">Game identifier.</param>
        /// <param name="ct">Optional cancellation token.</param>
        /// <returns>Latest deployed game version.</returns>
        public static Task<string> GetLatestVersionAsync(string gameId, CancellationToken ct = default) =>
            Api.GetLatestVersionAsync(gameId, ct);

        /// <summary>
        /// Subscribes to incoming events of type <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">Event payload type.</typeparam>
        /// <returns>Observable stream of events.</returns>
        public static IObservable<T> Subscribe<T>() =>
            Api.Subscribe<T>();

        /// <summary>
        /// Subscribes to incoming events of type <typeparamref name="T"/> with callback.
        /// </summary>
        /// <typeparam name="T">Event payload type.</typeparam>
        /// <param name="onNext">Callback invoked for every received event.</param>
        /// <returns>Subscription handle that should be disposed when no longer needed.</returns>
        public static IDisposable Subscribe<T>(Action<T> onNext) =>
            Api.Subscribe(onNext);

        /// <summary>
        /// Sends command object using default command namespace resolution.
        /// </summary>
        /// <typeparam name="T">Command type.</typeparam>
        /// <param name="command">Command payload.</param>
        public static void Send<T>(T command) =>
            Api.Send(command);

        /// <summary>
        /// Sets optional local command handler for server-side/in-process execution.
        /// </summary>
        /// <param name="commandHandler">Local command handler. Pass null to disable local handling.</param>
        public static void SetCommandHandler(ICommandHandler commandHandler) =>
            Api.SetCommandHandler(commandHandler);

        /// <summary>
        /// Sends command object to explicit backend module/command path.
        /// </summary>
        /// <typeparam name="T">Command type.</typeparam>
        /// <param name="command">Command payload.</param>
        /// <param name="moduleName">Target module path, for example "rpc.InvokeRpc" or "module_dataflow".</param>
        public static void Send<T>(T command, string moduleName) =>
            Api.Send(command,  moduleName);

        /// <summary>
        /// Sets optional local event handler for server-side/in-process execution.
        /// </summary>
        /// <param name="eventHandler">Local event handler. Pass null to disable local handling.</param>
        public static void SetEventHandler(IEventHandler eventHandler) =>
            Api.SetEventHandler(eventHandler);

        /// <summary>
        /// Invokes server RPC method using object payload serialized to base64 JSON.
        /// </summary>
        /// <param name="serviceName">RPC service name.</param>
        /// <param name="methodName">RPC method name.</param>
        /// <param name="payload">Payload object to serialize.</param>
        public static void Invoke(string serviceName, string methodName, object payload) =>
            Api.Invoke(serviceName, methodName, payload);

        /// <summary>
        /// Invokes server RPC method using positional argument array without expression parsing.
        /// </summary>
        /// <param name="serviceName">RPC service name.</param>
        /// <param name="methodName">RPC method name.</param>
        /// <param name="args">Positional RPC arguments in declared method order.</param>
        public static void InvokeArgs(string serviceName, string methodName, params object[] args) =>
            Api.InvokeArgs(serviceName, methodName, args);

        /// <summary>
        /// Invokes server RPC method using named argument payload without expression parsing.
        /// </summary>
        /// <param name="serviceName">RPC service name.</param>
        /// <param name="methodName">RPC method name.</param>
        /// <param name="payload">Named RPC arguments keyed by parameter name.</param>
        public static void InvokeNamed(string serviceName, string methodName, IDictionary<string, object> payload) =>
            Api.InvokeNamed(serviceName, methodName, payload);

        /// <summary>
        /// Sets optional local RPC invoker for server-side/in-process execution.
        /// </summary>
        /// <param name="rpcInvoker">Local invoker implementation. Pass null to disable local invocation.</param>
        public static void SetRpcInvoker(IRpcInvoker rpcInvoker) =>
            Api.SetRpcInvoker(rpcInvoker);

        /// <summary>
        /// Invokes server RPC method using already prepared base64 JSON payload.
        /// </summary>
        /// <param name="serviceName">RPC service name.</param>
        /// <param name="methodName">RPC method name.</param>
        /// <param name="payloadBase64">Base64-encoded UTF8 JSON payload.</param>
        public static void Invoke(string serviceName, string methodName, string payloadBase64) =>
            Api.Invoke(serviceName, methodName, payloadBase64);

        /// <summary>
        /// Invokes server RPC method from a method-call expression and auto-builds payload from arguments.
        /// </summary>
        /// <typeparam name="TService">RPC service type used to derive service name.</typeparam>
        /// <param name="method">Method call expression, e.g. x => x.BroadcastToAll("Hello"). Service class must have [Rpc] attribute.</param>
        public static void Invoke<TService>(Expression<Action<TService>> method) =>
            Api.Invoke(method);

        /// <summary>
        /// Invokes server RPC method by passing method expression of service type.
        /// </summary>
        /// <typeparam name="TService">RPC service type used to derive service name.</typeparam>
        /// <param name="method">Method call expression, e.g. x => x.BroadcastToAll(default). Service class must have [Rpc] attribute.</param>
        /// <param name="payload">Payload object to serialize to base64 JSON.</param>
        public static void Invoke<TService>(Expression<Action<TService>> method, object payload) =>
            Api.Invoke(method, payload);

        /// <summary>
        /// Invokes server RPC method by passing method expression with pre-encoded base64 payload.
        /// </summary>
        /// <typeparam name="TService">RPC service type used to derive service name.</typeparam>
        /// <param name="method">Method call expression, e.g. x => x.BroadcastToAll(default). Service class must have [Rpc] attribute.</param>
        /// <param name="payloadBase64">Base64-encoded UTF8 JSON payload.</param>
        public static void Invoke<TService>(Expression<Action<TService>> method, string payloadBase64) =>
            Api.Invoke(method, payloadBase64);

        /// <summary>
        /// Returns low-level transport implementation used by SDK.
        /// Intended for testing and protocol diagnostics only.
        /// </summary>
        public static Playserv.Proxy.Interfaces.ITransportImplementation GetTransportImplementation() =>
            Api.GetTransportImplementation();

        /// <summary>
        /// Publishes global event to all interested listeners.
        /// </summary>
        /// <typeparam name="T">Event payload type.</typeparam>
        /// <param name="event">Event payload.</param>
        public static void Publish<T>(T @event) =>
            Api.Publish(@event);

        /// <summary>
        /// Publishes event scoped to a specific group.
        /// </summary>
        /// <typeparam name="T">Event payload type.</typeparam>
        /// <param name="groupName">Target group name.</param>
        /// <param name="event">Event payload.</param>
        public static void PublishForGroup<T>(string groupName, T @event) =>
            Api.PublishForGroup(groupName, @event);

        /// <summary>
        /// Publishes event scoped to a specific user.
        /// </summary>
        /// <typeparam name="T">Event payload type.</typeparam>
        /// <param name="userId">Target user id.</param>
        /// <param name="event">Event payload.</param>
        public static void PublishForUser<T>(string userId, T @event) =>
            Api.PublishForUser(userId, @event);

        /// <summary>
        /// Joins named event group for group-scoped routing.
        /// </summary>
        /// <param name="groupName">Target group name.</param>
        /// <param name="ct">Optional cancellation token.</param>
        /// <returns>True when group join succeeded.</returns>
        public static Task<bool> SubscribeGroupAsync(string groupName, CancellationToken ct = default) =>
            Api.SubscribeGroupAsync(groupName, ct);

        /// <summary>
        /// Leaves named event group.
        /// </summary>
        /// <param name="groupName">Target group name.</param>
        /// <param name="ct">Optional cancellation token.</param>
        /// <returns>True when group leave succeeded.</returns>
        public static Task<bool> UnsubscribeGroupAsync(string groupName, CancellationToken ct = default) =>
            Api.UnsubscribeGroupAsync(groupName, ct);

        /// <summary>
        /// Disconnects SDK transport and disposes internal runtime instance.
        /// </summary>
        public static void Disconnect() =>
            Api.Disconnect();

#if UNITY_5_3_OR_NEWER
        /// <summary>
        /// Spawns networked prefab from Resources at given position and rotation.
        /// </summary>
        /// <param name="assetName">Path inside Resources folder.</param>
        /// <param name="position">World position.</param>
        /// <param name="rotation">World rotation.</param>
        /// <returns>Spawned GameObject or null when spawn failed.</returns>
        public static Task<GameObject> Spawn(string assetName, Vector3 position, Quaternion rotation) =>
            Api.Spawn(assetName, position, rotation);

        /// <summary>
        /// Spawns networked prefab from Resources with identity rotation.
        /// </summary>
        /// <param name="assetName">Path inside Resources folder.</param>
        /// <param name="position">World position.</param>
        /// <returns>Spawned GameObject or null when spawn failed.</returns>
        public static Task<GameObject> Spawn(string assetName, Vector3 position) =>
            Api.Spawn(assetName, position);
#endif

        /// <summary>
        /// Creates shared data subscription for a player entity and maps server model to DTO.
        /// </summary>
        /// <typeparam name="TEntity">Raw entity model type returned by backend.</typeparam>
        /// <typeparam name="TDto">Client DTO type used by gameplay/UI.</typeparam>
        /// <param name="playerId">Target player identifier used as entity key.</param>
        /// <param name="map">Projection function from entity model to DTO.</param>
        /// <param name="mode">Subscription backend mode. Defaults to Polling.</param>
        /// <returns>Shared entity handle with updates, mutations and refresh operations.</returns>
        public static Task<ISharedEntity<TDto>> SelectEntity<TEntity, TDto>(
            string playerId,
            Func<TEntity, TDto> map,
            DataSubscriptionMode mode = DataSubscriptionMode.Polling)
            where TEntity : class
            where TDto : class, new() =>
            Api.SelectEntity<TEntity, TDto>(playerId, map, mode);

        /// <summary>
        /// Sends simplified key-based retrieval request once.
        /// </summary>
        /// <param name="key">Entity key.</param>
        /// <param name="query">GraphQL-like query string.</param>
        /// <param name="variables">Query variables object.</param>
        /// <param name="ct">Optional cancellation token.</param>
        /// <returns>DataGetResponse with Result or Error.</returns>
        public static Task<DataGetResponse> GetDataByKeyAsync(
            string key,
            string query,
            Dictionary<string, object> variables,
            CancellationToken ct = default) =>
            Api.GetDataByKeyAsync(key, query, variables, ct);

        /// <summary>
        /// Starts simplified key-based polling loop.
        /// </summary>
        /// <param name="key">Entity key.</param>
        /// <param name="query">GraphQL-like query string.</param>
        /// <param name="variables">Query variables object.</param>
        /// <param name="onData">Callback invoked for each response.</param>
        /// <param name="onError">Optional callback for runtime errors.</param>
        /// <returns>Disposable handle to stop polling.</returns>
        public static IDisposable StartDataByKeyPolling(
            string key,
            string query,
            Dictionary<string, object> variables,
            Action<DataGetResponse> onData,
            Action<Exception> onError = null) =>
            Api.StartDataByKeyPolling(key, query, variables, onData, onError);
    }
}
