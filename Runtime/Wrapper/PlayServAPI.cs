using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Playserv.DataSubscription;
using Playserv.DataSubscription.Responses;
using Playserv.Proxy.Common;
using Playserv.Proxy.Interfaces;
using Playserv.Proxy.Logging;
using Playserv.RPC;
using Playserv.Runtime.Abstractions;
using Playserv.Server;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

namespace Playserv.Wrapper
{
    internal sealed class PlayServApi : IPlayServApi, IPlayServConnectionApi, IPlayServRpcApi, IPlayServEventsApi, IPlayServDataApi
#if UNITY_5_3_OR_NEWER
        , IPlayServSpawnApi
#endif
    {
        private const int ConnectVersionRefreshTimeoutSeconds = 5;
        private int _shutdownIgnoreWarningLogged;
        private readonly PlayServApiLocalExecutionFacade _localExecution;
        private readonly PlayServApiRpcFacade _rpcFacade;
        private readonly PlayServApiEventsFacade _eventsFacade;
        private readonly PlayServApiDataFacade _dataFacade;
        private readonly PlayServApiConfigFacade _configFacade;
        private readonly PlayServApiConnectionOrchestrator _connectionOrchestrator;

        public string SdkVersion => SdkInfo.Version;
        public PlayServSettings Settings => _configFacade.Settings;

        public PlayServState State => _configFacade.State;

        public event Action<TransportError> OnTransportError;
        public event Action OnKeepAlivePingSent;
        public event Action OnKeepAlivePongReceived;
        public event Action<InvokeRpcResponse> OnRpcInvokeResponse;

        public PlayServApi()
        {
            _localExecution = new PlayServApiLocalExecutionFacade();
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
            _eventsFacade = new PlayServApiEventsFacade(
                _localExecution,
                () => _configFacade.CurrentInstance,
                GetInstanceForFireAndForget,
                () => Instance);
            _dataFacade = new PlayServApiDataFacade(() => Instance);
            _rpcFacade = new PlayServApiRpcFacade(
                _localExecution,
                () => _configFacade.CurrentInstance,
                GetInstanceForFireAndForget);
        }

        public void Config(PlayServSettings settings) => _configFacade.Config(settings);

        public void Config(string gameAccessToken, string gameId, string userId, string gameVersion, string sdkVersion = null) =>
            _configFacade.Config(gameAccessToken, gameId, userId, gameVersion, sdkVersion);

        public Task<bool> Connect() => _connectionOrchestrator.ConnectAsync();

        public void SetWebRtcSignalingClientFactory(Func<PlayServRuntimeSettings, IWebRtcSignalingClient> signalingClientFactory) =>
            _configFacade.SetWebRtcSignalingClientFactory(signalingClientFactory);

        public Task<string> GetLatestVersionAsync(string gameId, CancellationToken ct = default) =>
            _configFacade.GetLatestVersionAsync(gameId, ct);

        public IDisposable Subscribe<T>(Action<T> onNext) => _eventsFacade.Subscribe(onNext);

        public IObservable<T> Subscribe<T>() => _eventsFacade.Subscribe<T>();

        public void Send<T>(T command)
        {
            if (_localExecution.TryHandleCommand(command, moduleName: null, _configFacade.CurrentInstance != null))
                return;

            if (!_connectionOrchestrator.TryGetInstanceForFireAndForget("command send", out var instance))
                return;

            instance.Send(command);
        }

        public void SetCommandHandler(ICommandHandler commandHandler) =>
            _localExecution.SetCommandHandler(commandHandler);

        public void Send<T>(T command, string moduleName)
        {
            if (_localExecution.TryHandleCommand(command, moduleName, _configFacade.CurrentInstance != null))
                return;

            if (!_connectionOrchestrator.TryGetInstanceForFireAndForget("command send", out var instance))
                return;

            instance.Send(command, moduleName);
        }

        public void SetEventHandler(IEventHandler eventHandler) =>
            _eventsFacade.SetEventHandler(eventHandler);

        public void Invoke(string serviceName, string methodName, object payload) =>
            _rpcFacade.Invoke(serviceName, methodName, payload);

        public void InvokeArgs(string serviceName, string methodName, params object[] args) =>
            _rpcFacade.InvokeArgs(serviceName, methodName, args);

        public void InvokeNamed(string serviceName, string methodName, IDictionary<string, object> payload) =>
            _rpcFacade.InvokeNamed(serviceName, methodName, payload);

        public void SetRpcInvoker(IRpcInvoker rpcInvoker) =>
            _localExecution.SetRpcInvoker(rpcInvoker);

        public void Invoke(string serviceName, string methodName, string payloadBase64) =>
            _rpcFacade.Invoke(serviceName, methodName, payloadBase64);

        public void Invoke<TService>(Expression<Action<TService>> method) =>
            _rpcFacade.Invoke(method);

        public void Invoke<TService>(Expression<Action<TService>> method, object payload) =>
            _rpcFacade.Invoke(method, payload);

        public void Invoke<TService>(Expression<Action<TService>> method, string payloadBase64) =>
            _rpcFacade.Invoke(method, payloadBase64);

        public Playserv.Proxy.Interfaces.ITransportImplementation GetTransportImplementation() =>
            Instance.GetTransportImplementation();

        public void Publish<T>(T @event) => _eventsFacade.Publish(@event);

        public void PublishForGroup<T>(string groupName, T @event) => _eventsFacade.PublishForGroup(groupName, @event);

        public void PublishForUser<T>(string userId, T @event) => _eventsFacade.PublishForUser(userId, @event);

        public Task<bool> SubscribeGroupAsync(string groupName, CancellationToken ct = default) =>
            _eventsFacade.SubscribeGroupAsync(groupName, ct);

        public Task<bool> UnsubscribeGroupAsync(string groupName, CancellationToken ct = default) =>
            _eventsFacade.UnsubscribeGroupAsync(groupName, ct);

#if UNITY_5_3_OR_NEWER
        public Task<GameObject> Spawn(string assetName, Vector3 position, Quaternion rotation) =>
            _dataFacade.Spawn(assetName, position, rotation);

        public Task<GameObject> Spawn(string assetName, Vector3 position) =>
            _dataFacade.Spawn(assetName, position);
#endif

        public void Disconnect() => _configFacade.Disconnect();

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
            instance.OnRpcInvokeResponse -= HandleRpcInvokeResponse;

            instance.OnTransportError += HandleTransportError;
            instance.OnKeepAlivePingSent += HandleKeepAlivePingSent;
            instance.OnKeepAlivePongReceived += HandleKeepAlivePongReceived;
            instance.OnRpcInvokeResponse += HandleRpcInvokeResponse;
        }

        private void HandleTransportError(TransportError error) => OnTransportError?.Invoke(error);
        private void HandleKeepAlivePingSent() => OnKeepAlivePingSent?.Invoke();
        private void HandleKeepAlivePongReceived() => OnKeepAlivePongReceived?.Invoke();
        private void HandleRpcInvokeResponse(InvokeRpcResponse response) => OnRpcInvokeResponse?.Invoke(response);

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
    }
}
