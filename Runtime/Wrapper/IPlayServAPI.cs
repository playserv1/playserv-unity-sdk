using System;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Playserv.DataSubscription;
using Playserv.Proxy.Common;
using Playserv.RPC;
using Playserv.Server;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

#nullable enable

namespace Playserv.Wrapper
{
    /// <summary>
    /// Abstraction for PlayServ runtime API implementation.
    /// </summary>
    public interface IPlayServApi
    {
        /// <summary>
        /// Gets current SDK version string.
        /// </summary>
        string SdkVersion { get; }

        /// <summary>
        /// Gets current connection state.
        /// </summary>
        PlayServState State { get; }

        /// <summary>
        /// Raised when transport-level error occurs.
        /// </summary>
        event Action<TransportError>? OnTransportError;

        /// <summary>
        /// Raised when keepalive ping is sent.
        /// </summary>
        event Action? OnKeepAlivePingSent;

        /// <summary>
        /// Raised when keepalive pong is received.
        /// </summary>
        event Action? OnKeepAlivePongReceived;

        /// <summary>
        /// Applies full SDK settings object.
        /// </summary>
        /// <param name="settings">Runtime settings for endpoint, auth and timeouts.</param>
        void Config(PlayServSettings settings);

        /// <summary>
        /// Applies basic connection settings.
        /// </summary>
        /// <param name="gameAccessToken">Access token for handshake.</param>
        /// <param name="gameId">Game identifier.</param>
        /// <param name="userId">User/player identifier.</param>
        /// <param name="gameVersion">Game client version.</param>
        /// <param name="sdkVersion">Optional SDK version override.</param>
        void Config(string gameAccessToken, string gameId, string userId, string gameVersion, string? sdkVersion = null);

        /// <summary>
        /// Connects transport and performs handshake.
        /// </summary>
        /// <returns>True if connected successfully; otherwise false.</returns>
        Task<bool> Connect();

        /// <summary>
        /// Subscribes to incoming events of type <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">Event payload type.</typeparam>
        /// <returns>Observable stream of events.</returns>
        IObservable<T> Subscribe<T>();

        /// <summary>
        /// Subscribes to incoming events of type <typeparamref name="T"/> using callback.
        /// </summary>
        /// <typeparam name="T">Event payload type.</typeparam>
        /// <param name="onNext">Callback for received event.</param>
        /// <returns>Disposable subscription handle.</returns>
        IDisposable Subscribe<T>(Action<T> onNext);

        /// <summary>
        /// Sends command using default command naming.
        /// </summary>
        /// <typeparam name="T">Command type.</typeparam>
        /// <param name="command">Command payload.</param>
        void Send<T>(T command);

        /// <summary>
        /// Sets optional local command handler for server-side/in-process execution.
        /// </summary>
        /// <param name="commandHandler">Local command handler. Pass null to disable local handling.</param>
        void SetCommandHandler(ICommandHandler? commandHandler);

        /// <summary>
        /// Sends command to explicit module path.
        /// </summary>
        /// <typeparam name="T">Command type.</typeparam>
        /// <param name="command">Command payload.</param>
        /// <param name="moduleName">Target module path.</param>
        void Send<T>(T command, string moduleName);

        /// <summary>
        /// Sets optional local event handler for server-side/in-process execution.
        /// </summary>
        /// <param name="eventHandler">Local event handler. Pass null to disable local handling.</param>
        void SetEventHandler(IEventHandler? eventHandler);

        /// <summary>
        /// Invokes server RPC method using object payload serialized to base64 JSON.
        /// </summary>
        /// <param name="serviceName">RPC service name.</param>
        /// <param name="methodName">RPC method name.</param>
        /// <param name="payload">Payload object to serialize.</param>
        void Invoke(string serviceName, string methodName, object? payload);

        /// <summary>
        /// Sets optional local RPC invoker for server-side/in-process execution.
        /// </summary>
        /// <param name="rpcInvoker">Local invoker implementation. Pass null to disable local invocation.</param>
        void SetRpcInvoker(IRpcInvoker? rpcInvoker);

        /// <summary>
        /// Invokes server RPC method using already prepared base64 JSON payload.
        /// </summary>
        /// <param name="serviceName">RPC service name.</param>
        /// <param name="methodName">RPC method name.</param>
        /// <param name="payloadBase64">Base64-encoded UTF8 JSON payload.</param>
        void Invoke(string serviceName, string methodName, string payloadBase64);

        /// <summary>
        /// Invokes server RPC method from a method-call expression and auto-builds payload from arguments.
        /// </summary>
        /// <typeparam name="TService">RPC service type used to derive service name.</typeparam>
        /// <param name="method">Method call expression, e.g. x => x.BroadcastToAll("Hello"). Service class must have [Rpc] attribute.</param>
        void Invoke<TService>(Expression<Action<TService>> method);

        /// <summary>
        /// Invokes server RPC method by passing method expression of service type.
        /// </summary>
        /// <typeparam name="TService">RPC service type used to derive service name.</typeparam>
        /// <param name="method">Method call expression, e.g. x => x.BroadcastToAll(default). Service class must have [Rpc] attribute.</param>
        /// <param name="payload">Payload object to serialize to base64 JSON.</param>
        void Invoke<TService>(Expression<Action<TService>> method, object? payload);

        /// <summary>
        /// Invokes server RPC method by passing method expression with pre-encoded base64 payload.
        /// </summary>
        /// <typeparam name="TService">RPC service type used to derive service name.</typeparam>
        /// <param name="method">Method call expression, e.g. x => x.BroadcastToAll(default). Service class must have [Rpc] attribute.</param>
        /// <param name="payloadBase64">Base64-encoded UTF8 JSON payload.</param>
        void Invoke<TService>(Expression<Action<TService>> method, string payloadBase64);

        /// <summary>
        /// Returns low-level transport implementation.
        /// Intended for tests and protocol diagnostics.
        /// </summary>
        Proxy.Interfaces.ITransportImplementation GetTransportImplementation();

        /// <summary>
        /// Publishes global event.
        /// </summary>
        /// <typeparam name="T">Event payload type.</typeparam>
        /// <param name="event">Event payload.</param>
        void Publish<T>(T @event);

        /// <summary>
        /// Publishes event to specific group.
        /// </summary>
        /// <typeparam name="T">Event payload type.</typeparam>
        /// <param name="groupName">Target group name.</param>
        /// <param name="event">Event payload.</param>
        void PublishForGroup<T>(string groupName, T @event);

        /// <summary>
        /// Publishes event to specific user.
        /// </summary>
        /// <typeparam name="T">Event payload type.</typeparam>
        /// <param name="userId">Target user id.</param>
        /// <param name="event">Event payload.</param>
        void PublishForUser<T>(string userId, T @event);

#if UNITY_5_3_OR_NEWER
        /// <summary>
        /// Spawns networked object at position and rotation.
        /// </summary>
        /// <param name="assetName">Path in Resources.</param>
        /// <param name="position">World position.</param>
        /// <param name="rotation">World rotation.</param>
        /// <returns>Spawned GameObject or null if failed.</returns>
        Task<GameObject> Spawn(string assetName, Vector3 position, Quaternion rotation);

        /// <summary>
        /// Spawns networked object with identity rotation.
        /// </summary>
        /// <param name="assetName">Path in Resources.</param>
        /// <param name="position">World position.</param>
        /// <returns>Spawned GameObject or null if failed.</returns>
        Task<GameObject> Spawn(string assetName, Vector3 position);
#endif

        /// <summary>
        /// Disconnects and disposes current SDK runtime instance.
        /// </summary>
        void Disconnect();

        /// <summary>
        /// Creates shared entity subscription and maps entity model to DTO.
        /// </summary>
        /// <typeparam name="TEntity">Backend entity model type.</typeparam>
        /// <typeparam name="TDto">Client DTO type.</typeparam>
        /// <param name="playerId">Entity key/player id.</param>
        /// <param name="map">Projection function from entity to DTO.</param>
        /// <returns>Shared entity instance for observing and mutating data.</returns>
        Task<ISharedEntity<TDto>> SelectEntity<TEntity, TDto>(string playerId, Func<TEntity, TDto> map)
            where TEntity : class
            where TDto : class, new();
    }
}

#nullable restore
