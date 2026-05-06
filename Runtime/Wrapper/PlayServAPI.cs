using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
#if !PLAYSERV_DISABLE_DATA && !PLAYSERV_DISABLE_EVENTS
using Playserv.DataSubscription;
using Playserv.DataSubscription.Responses;
#endif
using Playserv.Proxy.Common;
using Playserv.Proxy.Interfaces;
using Playserv.Proxy.Logging;
#if !PLAYSERV_DISABLE_RPC_CORE && (!PLAYSERV_DISABLE_CLIENT_RPC || !PLAYSERV_DISABLE_SERVER_RPC)
using Playserv.RPC;
#endif
using Playserv.Runtime.Abstractions;
#if !PLAYSERV_DISABLE_LOCAL_EXECUTION_CORE
using Playserv.Server;
#endif
using Playserv.Serialization;
#if UNITY_5_3_OR_NEWER && !PLAYSERV_DISABLE_SPAWN && !PLAYSERV_DISABLE_EVENTS
using Playserv.Spawn;
#endif
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

namespace Playserv.Wrapper
{
    internal sealed class PlayServApi : IPlayServConnectionApi
#if !PLAYSERV_DISABLE_RPC_CORE && !PLAYSERV_DISABLE_CLIENT_RPC
        , IPlayServRpcApi
#endif
#if !PLAYSERV_DISABLE_LOCAL_EXECUTION_CORE && !PLAYSERV_DISABLE_RPC_CORE && !PLAYSERV_DISABLE_SERVER_RPC && !PLAYSERV_DISABLE_LOCAL_EXECUTION_SERVER
        , IPlayServServerRpcApi
#endif
#if !PLAYSERV_DISABLE_EVENTS
        , IPlayServEventsApi
#endif
#if !PLAYSERV_DISABLE_DATA && !PLAYSERV_DISABLE_EVENTS
        , IPlayServDataApi
#endif
#if UNITY_5_3_OR_NEWER && !PLAYSERV_DISABLE_SPAWN && !PLAYSERV_DISABLE_EVENTS
        , IPlayServSpawnApi
#endif
    {
        private const int ConnectVersionRefreshTimeoutSeconds = 5;
        private int _shutdownIgnoreWarningLogged;
#if !PLAYSERV_DISABLE_LOCAL_EXECUTION_CORE
        private readonly IPlayServLocalExecution _localExecution;
#endif
#if !PLAYSERV_DISABLE_RPC_CORE && !PLAYSERV_DISABLE_CLIENT_RPC
        private readonly PlayServApiRpcFacade _rpcFacade;
#endif
#if !PLAYSERV_DISABLE_EVENTS
        private readonly PlayServApiEventsFacade _eventsFacade;
#endif
#if !PLAYSERV_DISABLE_DATA && !PLAYSERV_DISABLE_EVENTS
        private readonly PlayServApiDataFacade _dataFacade;
#endif
#if UNITY_5_3_OR_NEWER && !PLAYSERV_DISABLE_SPAWN && !PLAYSERV_DISABLE_EVENTS
        private INetworkPrefabRegistry _spawnPrefabRegistry;
#endif
        private readonly PlayServApiConfigFacade _configFacade;
        private readonly PlayServApiConnectionOrchestrator _connectionOrchestrator;

        public string SdkVersion => SdkInfo.Version;
        public PlayServSettings Settings => _configFacade.Settings;

        public PlayServState State => _configFacade.State;

        public event Action<TransportError> OnTransportError;
        public event Action OnKeepAlivePingSent;
        public event Action OnKeepAlivePongReceived;
#if !PLAYSERV_DISABLE_RPC_CORE && !PLAYSERV_DISABLE_CLIENT_RPC
        public event Action<InvokeRpcResponse> OnRpcInvokeResponse;
#endif

        public PlayServApi()
        {
#if !PLAYSERV_DISABLE_LOCAL_EXECUTION_CORE
            _localExecution = PlayServLocalExecutionFactory.Create();
#endif
            _configFacade = new PlayServApiConfigFacade(
                subscribeToInstanceEvents: SubscribeToInstanceEvents,
                logTrace: message => PlayServLog.Trace(PlayServLogCategory.General, message),
                versionRefreshTimeoutSeconds: ConnectVersionRefreshTimeoutSeconds);
            _connectionOrchestrator = new PlayServApiConnectionOrchestrator(
                getState: () => State,
                getOrCreateSettings: _configFacade.GetOrCreateSettings,
                refreshConfiguredGameVersionAsync: _configFacade.RefreshConfiguredGameVersionAsync,
                applySettings: _configFacade.ApplySettings,
                getCurrentInstance: () => _configFacade.CurrentInstance,
                disconnect: _configFacade.Disconnect,
                resetShutdownState: ResetShutdownState,
                logTrace: message => PlayServLog.Trace(PlayServLogCategory.General, message),
                shouldIgnoreMissingInstance: ShouldIgnoreMissingInstance,
                logShutdownIgnoreWarning: LogShutdownIgnoreWarning);
#if !PLAYSERV_DISABLE_EVENTS
            _eventsFacade = new PlayServApiEventsFacade(
#if !PLAYSERV_DISABLE_LOCAL_EXECUTION_CORE
                _localExecution,
#endif
                () => _configFacade.CurrentInstance,
                GetInstanceForFireAndForget,
                () => Instance);
#endif
#if !PLAYSERV_DISABLE_DATA && !PLAYSERV_DISABLE_EVENTS
            _dataFacade = new PlayServApiDataFacade(() => Instance);
#endif
#if !PLAYSERV_DISABLE_RPC_CORE && !PLAYSERV_DISABLE_CLIENT_RPC
            _rpcFacade = new PlayServApiRpcFacade(
#if !PLAYSERV_DISABLE_LOCAL_EXECUTION_CORE
                _localExecution,
#endif
                () => _configFacade.CurrentInstance,
                GetInstanceForFireAndForget,
                ResolveJsonCodec);
#endif
        }

        public void Config(PlayServSettings settings) => _configFacade.Config(settings);

        public void Config(string gameAccessToken, string gameId, string userId, string gameVersion, string sdkVersion = null) =>
            _configFacade.Config(gameAccessToken, gameId, userId, gameVersion, sdkVersion);

        public Task<bool> Connect() => _connectionOrchestrator.ConnectAsync();

        public void SetWebRtcSignalingClientFactory(Func<PlayServRuntimeSettings, IWebRtcSignalingClient> signalingClientFactory) =>
            _configFacade.SetWebRtcSignalingClientFactory(signalingClientFactory);

        public Task<string> GetLatestVersionAsync(string gameId, CancellationToken ct = default) =>
            _configFacade.GetLatestVersionAsync(gameId, ct);

#if !PLAYSERV_DISABLE_EVENTS
        public IDisposable Subscribe<T>(Action<T> onNext) => _eventsFacade.Subscribe(onNext);

        public IObservable<T> Subscribe<T>() => _eventsFacade.Subscribe<T>();
#endif

#if !PLAYSERV_DISABLE_RPC_CORE && !PLAYSERV_DISABLE_CLIENT_RPC
        public void Send<T>(T command)
        {
#if !PLAYSERV_DISABLE_LOCAL_EXECUTION_CORE
            if (_localExecution.TryHandleCommand(command, moduleName: null, _configFacade.CurrentInstance != null))
                return;
#endif

            if (!_connectionOrchestrator.TryGetInstanceForFireAndForget("command send", out var instance))
                return;

            instance.Send(command);
        }

#if !PLAYSERV_DISABLE_LOCAL_EXECUTION_CORE && !PLAYSERV_DISABLE_LOCAL_EXECUTION_SERVER
        public void SetCommandHandler(ICommandHandler commandHandler) =>
            _localExecution.SetCommandHandler(commandHandler);
#endif

        public void Send<T>(T command, string moduleName)
        {
#if !PLAYSERV_DISABLE_LOCAL_EXECUTION_CORE
            if (_localExecution.TryHandleCommand(command, moduleName, _configFacade.CurrentInstance != null))
                return;
#endif

            if (!_connectionOrchestrator.TryGetInstanceForFireAndForget("command send", out var instance))
                return;

            instance.Send(command, moduleName);
        }

#endif

#if !PLAYSERV_DISABLE_EVENTS
#if !PLAYSERV_DISABLE_LOCAL_EXECUTION_CORE && !PLAYSERV_DISABLE_LOCAL_EXECUTION_SERVER
        public void SetEventHandler(IEventHandler eventHandler) =>
            _eventsFacade.SetEventHandler(eventHandler);
#endif
#endif

#if !PLAYSERV_DISABLE_RPC_CORE && !PLAYSERV_DISABLE_CLIENT_RPC
        public void Invoke(string serviceName, string methodName, object payload) =>
            _rpcFacade.Invoke(serviceName, methodName, payload);

        public void InvokeArgs(string serviceName, string methodName, params object[] args) =>
            _rpcFacade.InvokeArgs(serviceName, methodName, args);

        public void InvokeNamed(string serviceName, string methodName, IDictionary<string, object> payload) =>
            _rpcFacade.InvokeNamed(serviceName, methodName, payload);

        public void Invoke(string serviceName, string methodName, string payloadBase64) =>
            _rpcFacade.Invoke(serviceName, methodName, payloadBase64);

        public void Invoke<TService>(Expression<Action<TService>> method) =>
            _rpcFacade.Invoke(method);

        public void Invoke<TService>(Expression<Action<TService>> method, object payload) =>
            _rpcFacade.Invoke(method, payload);

        public void Invoke<TService>(Expression<Action<TService>> method, string payloadBase64) =>
            _rpcFacade.Invoke(method, payloadBase64);
#endif

#if !PLAYSERV_DISABLE_RPC_CORE && !PLAYSERV_DISABLE_SERVER_RPC
#if !PLAYSERV_DISABLE_LOCAL_EXECUTION_CORE && !PLAYSERV_DISABLE_LOCAL_EXECUTION_SERVER
        public void SetRpcInvoker(IRpcInvoker rpcInvoker) =>
            _localExecution.SetRpcInvoker(rpcInvoker);
#endif
#endif

        public Playserv.Proxy.Interfaces.ITransportImplementation GetTransportImplementation() =>
            Instance.GetTransportImplementation();

#if !PLAYSERV_DISABLE_EVENTS
        public void Publish<T>(T @event) => _eventsFacade.Publish(@event);

        public void PublishForGroup<T>(string groupName, T @event) => _eventsFacade.PublishForGroup(groupName, @event);

        public void PublishForUser<T>(string userId, T @event) => _eventsFacade.PublishForUser(userId, @event);

        public Task<bool> SubscribeGroupAsync(string groupName, CancellationToken ct = default) =>
            _eventsFacade.SubscribeGroupAsync(groupName, ct);

        public Task<bool> UnsubscribeGroupAsync(string groupName, CancellationToken ct = default) =>
            _eventsFacade.UnsubscribeGroupAsync(groupName, ct);
#endif

#if UNITY_5_3_OR_NEWER && !PLAYSERV_DISABLE_SPAWN && !PLAYSERV_DISABLE_EVENTS
        public Task<GameObject> Spawn(string assetName, Vector3 position, Quaternion rotation) =>
            Instance.Spawn(assetName, position, rotation);

        public Task<GameObject> Spawn(string assetName, Vector3 position) =>
            Instance.Spawn(assetName, position);

        public string CurrentSpawnScope =>
            _configFacade.CurrentInstance?.CurrentSpawnScope;

        public Task<bool> JoinSpawnScopeAsync(string groupName, CancellationToken ct = default) =>
            Instance.JoinSpawnScopeAsync(groupName, ct);

        public Task<bool> LeaveSpawnScopeAsync(CancellationToken ct = default) =>
            Instance.LeaveSpawnScopeAsync(ct);

        public bool Despawn(string spawnId) =>
            Instance.Despawn(spawnId);

        public bool Despawn(GameObject instance) =>
            Instance.Despawn(instance);

        public void SetSpawnPrefabRegistry(INetworkPrefabRegistry prefabRegistry)
        {
            _spawnPrefabRegistry = prefabRegistry;
            _configFacade.CurrentInstance?.SetSpawnPrefabRegistry(prefabRegistry);
        }
#endif

        public void Disconnect() => _configFacade.Disconnect();

#if !PLAYSERV_DISABLE_DATA && !PLAYSERV_DISABLE_EVENTS
        public Task<ISharedEntity<TDto>> SelectEntity<TEntity, TDto>(
            string playerId,
            Func<TEntity, TDto> map,
            DataSubscriptionMode mode = DataSubscriptionMode.Polling)
            where TEntity : class
            where TDto : class, new() =>
            _dataFacade.SelectEntity<TEntity, TDto>(playerId, map, mode);

        public Task<DataGetResponse> GetDataByKeyAsync(
            string key,
            string query,
            Dictionary<string, object> variables,
            CancellationToken ct = default) =>
            _dataFacade.GetDataByKeyAsync(key, query, variables, ct);

        public IDisposable StartDataByKeyPolling(
            string key,
            string query,
            Dictionary<string, object> variables,
            Action<DataGetResponse> onData,
            Action<Exception> onError = null) =>
            _dataFacade.StartDataByKeyPolling(key, query, variables, onData, onError);
#endif

        private PlayServImplementation Instance =>
            _configFacade.CurrentInstance ?? throw new InvalidOperationException("SDK is not connected. Call Connect() first.");

        private void LogShutdownIgnoreWarning(string operationName)
        {
            if (Interlocked.Exchange(ref _shutdownIgnoreWarningLogged, 1) != 0)
                return;

#if UNITY_5_3_OR_NEWER
            PlayServLog.Warning(
                PlayServLogCategory.General,
                $"Ignoring {operationName} because Unity is shutting down or exiting play mode.");
#endif
        }

        private void SubscribeToInstanceEvents(PlayServImplementation instance)
        {
            if (instance == null)
                throw new ArgumentNullException(nameof(instance));

            instance.OnTransportError -= HandleTransportError;
            instance.OnKeepAlivePingSent -= HandleKeepAlivePingSent;
            instance.OnKeepAlivePongReceived -= HandleKeepAlivePongReceived;
#if !PLAYSERV_DISABLE_RPC_CORE && !PLAYSERV_DISABLE_CLIENT_RPC
            instance.OnRpcInvokeResponse -= HandleRpcInvokeResponse;
#endif

            instance.OnTransportError += HandleTransportError;
            instance.OnKeepAlivePingSent += HandleKeepAlivePingSent;
            instance.OnKeepAlivePongReceived += HandleKeepAlivePongReceived;
#if !PLAYSERV_DISABLE_RPC_CORE && !PLAYSERV_DISABLE_CLIENT_RPC
            instance.OnRpcInvokeResponse += HandleRpcInvokeResponse;
#endif
#if UNITY_5_3_OR_NEWER && !PLAYSERV_DISABLE_SPAWN && !PLAYSERV_DISABLE_EVENTS
            if (_spawnPrefabRegistry != null)
                instance.SetSpawnPrefabRegistry(_spawnPrefabRegistry);
#endif
        }

        private void HandleTransportError(TransportError error) => OnTransportError?.Invoke(error);
        private void HandleKeepAlivePingSent() => OnKeepAlivePingSent?.Invoke();
        private void HandleKeepAlivePongReceived() => OnKeepAlivePongReceived?.Invoke();
#if !PLAYSERV_DISABLE_RPC_CORE && !PLAYSERV_DISABLE_CLIENT_RPC
        private void HandleRpcInvokeResponse(InvokeRpcResponse response) => OnRpcInvokeResponse?.Invoke(response);
#endif

#if UNITY_5_3_OR_NEWER
        private void ResetShutdownState()
        {
            PlayServRuntimeShutdownState.Reset();
            Interlocked.Exchange(ref _shutdownIgnoreWarningLogged, 0);
        }

        private bool ShouldIgnoreMissingInstance()
        {
            return !Application.isPlaying || PlayServRuntimeShutdownState.IsShuttingDown;
        }
#else
        private void ResetShutdownState()
        {
            Interlocked.Exchange(ref _shutdownIgnoreWarningLogged, 0);
        }

        private static bool ShouldIgnoreMissingInstance() => false;
#endif

        private PlayServImplementation GetInstanceForFireAndForget(string operationName)
        {
            return _connectionOrchestrator.TryGetInstanceForFireAndForget(operationName, out var instance)
                ? instance
                : null;
        }

#if !PLAYSERV_DISABLE_RPC_CORE && !PLAYSERV_DISABLE_CLIENT_RPC
        private IJsonCodec ResolveJsonCodec()
        {
            var instance = _configFacade.CurrentInstance;
            if (instance != null && instance.ModuleServices.TryGet<IJsonCodec>(out var jsonCodec))
                return jsonCodec;

            return null;
        }
#endif
    }
}
