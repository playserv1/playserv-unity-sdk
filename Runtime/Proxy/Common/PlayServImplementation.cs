using System;
using System.Threading;
using System.Threading.Tasks;
using Playserv.DataSubscription;
using Playserv.Events;
using Playserv.Proxy.Implementation;
using Playserv.Proxy.Interfaces;
using Playserv.Proxy.Logging;

namespace Playserv.Proxy.Common
{
    public sealed partial class PlayServImplementation : IDisposable
    {
        private readonly ITransport _transport;
        private readonly ILogger _logger;
        private readonly IEventsAdapter _eventsAdapter;
        private readonly IDataSubscriptionAdapter _dataSubscriptionAdapter;
        private readonly ReconnectionManager _reconnectionManager;
        private readonly HandshakeService _handshakeService;
        private readonly KeepAliveManager _keepAliveManager;
        private readonly SynchronizationContext _mainThreadContext;

        private string _gameAccessToken;
        private string _gameId;
        private string _userId;
        private string _gameVersion;
        private string _sdkVersion = SdkInfo.Version;
        private bool _disconnectedByServer;
        private bool _allowMultipleConnections = true;

        public PlayServState State { get; private set; } = PlayServState.Offline;

        public event Action<TransportError> OnTransportError;
        public event Action OnKeepAlivePingSent;
        public event Action OnKeepAlivePongReceived;

        public PlayServImplementation(string endpoint)
            : this(endpoint, new JsonSerializer(), new RequestIdGenerator(), new ConsoleLogger()) { }

        private PlayServImplementation(
            string endpoint,
            IMessageSerializer serializer,
            IRequestIdGenerator requestIdGenerator,
            ILogger logger,
            Func<string, ITransportImplementation> transportImplementationFactory = null)
        {
            if (serializer == null)
                throw new ArgumentNullException(nameof(serializer));

            if (requestIdGenerator == null)
                throw new ArgumentNullException(nameof(requestIdGenerator));

            if (logger == null)
                throw new ArgumentNullException(nameof(logger));

            if (string.IsNullOrWhiteSpace(endpoint))
                throw new ArgumentException("Endpoint cannot be null or empty.", nameof(endpoint));

            _logger = logger;
            _mainThreadContext = SynchronizationContext.Current;

            if (transportImplementationFactory == null)
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                transportImplementationFactory = ep => new WebGLWebSocketTransportImplementation(ep, logger);
#else
                transportImplementationFactory = ep => new WebSocketTransportImplementation(ep, logger);
#endif
            }

            var implementation = transportImplementationFactory(endpoint);

            _transport = new Transport(implementation, serializer, requestIdGenerator, logger);
            _eventsAdapter = new PlayServEventsAdapter(_transport, _logger);
            _dataSubscriptionAdapter = new PlayServDataSubscriptionAdapter(this, _logger);
            _reconnectionManager = new ReconnectionManager(_transport, _logger, state => State = state, IsReconnectEnvironmentReadyAsync, ReconnectSessionAsync);
            _handshakeService = new HandshakeService(_transport, _logger);
            _keepAliveManager = new KeepAliveManager(_transport, _logger);

            _handshakeService.OnError += HandleTransportError;
            _keepAliveManager.OnTimeout += HandleKeepAliveTimeout;
            _keepAliveManager.OnPingSent += () => OnKeepAlivePingSent?.Invoke();
            _keepAliveManager.PongReceived += () => OnKeepAlivePongReceived?.Invoke();

            if (_transport is Transport transport)
            {
                transport.ConnectionLost += OnConnectionLost;
            }

            SetupCommandHandlers();
        }

        private Task<bool> IsReconnectEnvironmentReadyAsync()
        {
#if UNITY_EDITOR
            if (_mainThreadContext != null)
            {
                var tcs = new TaskCompletionSource<bool>();
                _mainThreadContext.Post(_ =>
                {
                    tcs.SetResult(UnityEngine.Application.isPlaying);
                }, null);
                return tcs.Task;
            }
            return Task.FromResult(false);
#else
            return Task.FromResult(true);
#endif
        }

        private async Task<bool> ReconnectSessionAsync()
        {
            var connected = await _transport.Connect();
            if (!connected)
                return false;
            
            var handshakeResult = await _handshakeService.PerformHandshakeAsync(_gameAccessToken, _gameId, _userId, _gameVersion, _sdkVersion);
            if (!handshakeResult.Success)
            {
                _logger.LogError($"Reconnection handshake failed: {handshakeResult.Error}");
                return false;
            }
            
            await SendClientSettingsAsync();
            _keepAliveManager.Start();
            InitializeSpawnManager();

            return true;
        }

        private void HandleTransportError(TransportError error)
        {
            _logger.LogError($"Transport error: {error}");
            OnTransportError?.Invoke(error);
        }

        private void HandleKeepAliveTimeout()
        {
            _logger.LogWarning("KeepAlive timeout. Connection may be lost.");
            _reconnectionManager.HandleConnectionLost();
        }

        public void SetConfig(
            string gameAccessToken,
            string gameId,
            string userId,
            string gameVersion,
            string sdkVersion = null,
            bool allowMultipleConnections = true,
            int keepAlivePingIntervalMs = 30000,
            int keepAlivePongTimeoutMs = 10000)
        {
            if (string.IsNullOrWhiteSpace(gameAccessToken))
                throw new ArgumentException("Game access token cannot be null or empty.", nameof(gameAccessToken));

            if (string.IsNullOrWhiteSpace(gameId))
                throw new ArgumentException("Game ID cannot be null or empty.", nameof(gameId));

            if (string.IsNullOrWhiteSpace(userId))
                throw new ArgumentException("User ID cannot be null or empty.", nameof(userId));

            if (string.IsNullOrWhiteSpace(gameVersion))
                throw new ArgumentException("Game version cannot be null or empty.", nameof(gameVersion));

            _gameAccessToken = gameAccessToken;
            _gameId = gameId;
            _userId = userId;
            _gameVersion = gameVersion;
            _sdkVersion = string.IsNullOrWhiteSpace(sdkVersion) ? SdkInfo.Version : sdkVersion;
            _allowMultipleConnections = allowMultipleConnections;
            _keepAliveManager.PingIntervalMs = keepAlivePingIntervalMs;
            _keepAliveManager.PongTimeoutMs = keepAlivePongTimeoutMs;

            _logger.Log($"Config set: token={gameAccessToken}, gameId={gameId}, userId={userId}, gameVersion={gameVersion}, sdkVersion={_sdkVersion}, allowMultiple={allowMultipleConnections}");
        }

        public async Task<bool> Connect()
        {
            if (string.IsNullOrWhiteSpace(_gameAccessToken) || string.IsNullOrWhiteSpace(_gameId) ||
                string.IsNullOrWhiteSpace(_userId) || string.IsNullOrWhiteSpace(_gameVersion))
                throw new InvalidOperationException("SDK is not configured. Call SetConfig() first.");

            _disconnectedByServer = false;
            _reconnectionManager.Start();

            State = PlayServState.Connecting;
            _logger.Log("Connecting to SDK...");

            var connected = await _transport.Connect();
            if (!connected)
            {
                State = PlayServState.Offline;
                _logger.LogError("Failed to connect to SDK.");
                return false;
            }

            State = PlayServState.Handshaking;
            _logger.Log("WebSocket connected. Performing handshake...");

            var handshakeResult = await _handshakeService.PerformHandshakeAsync(_gameAccessToken, _gameId, _userId, _gameVersion, _sdkVersion);
            if (!handshakeResult.Success)
            {
                _logger.LogError($"Handshake failed: {handshakeResult.Error}");
                OnTransportError?.Invoke(handshakeResult.Error);
                State = PlayServState.Offline;
                return false;
            }

            await SendClientSettingsAsync();
            _keepAliveManager.Start();
            InitializeSpawnManager();

            State = PlayServState.Online;
            _logger.Log("SDK connection established successfully. Ready for login.");
            return true;
        }

        public IDisposable On<T>(Action<T> onNext)
        {
            if (onNext == null)
                throw new ArgumentNullException(nameof(onNext));

            return On<T>().Subscribe(onNext);
        }

        public void Send<T>(T command, string moduleName = null)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            _ = _transport.Send(command, moduleName);
        }

        public async Task SendAsync<T>(T command, string moduleName = null)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            await _transport.Send(command, moduleName);
        }

        public IObservable<T> On<T>()
        {
            return _transport.OnReceive<T>();
        }

        public IObservable<object> OnCommand(string commandName)
        {
            if (string.IsNullOrWhiteSpace(commandName))
                throw new ArgumentException("Command name cannot be null or empty.", nameof(commandName));

            return _transport.OnReceive(commandName);
        }

        public IDisposable OnCommand(string commandName, Action<object> onNext)
        {
            if (onNext == null)
                throw new ArgumentNullException(nameof(onNext));

            return OnCommand(commandName).Subscribe(onNext);
        }

        public IObservable<T> Subscribe<T>()
        {
            return _eventsAdapter.Subscribe<T>();
        }

        public IDisposable Subscribe<T>(Action<T> onNext)
        {
            return _eventsAdapter.Subscribe(onNext);
        }

        public void Publish<T>(T @event)
        {
            _eventsAdapter.Publish(@event);
        }

        public void PublishForGroup<T>(string groupName, T @event)
        {
            _eventsAdapter.PublishForGroup(groupName, @event);
        }

        public void PublishForUser<T>(string userId, T @event)
        {
            _eventsAdapter.PublishForUser(userId, @event);
        }

        public ITransportImplementation GetTransportImplementation()
        {
            if (_transport is Transport transport)
            {
                return transport.GetImplementation();
            }
            return null;
        }

        internal ITransport GetTransport()
        {
            return _transport;
        }

        internal ILogger GetLogger()
        {
            return _logger;
        }

        public Task<ISharedEntity<TDto>> SelectEntity<TEntity, TDto>(string playerId, Func<TEntity, TDto> map)
            where TEntity : class
            where TDto : class, new()
        {
            return _dataSubscriptionAdapter.SelectEntity(playerId, map);
        }

        private void OnConnectionLost(object sender, EventArgs e)
        {
            _logger.LogWarning(
                $"[PlayServ][Connection] Transport connection lost event received. state={State}, disconnectedByServer={_disconnectedByServer}");
            _keepAliveManager.Stop();

            if (!_disconnectedByServer)
            {
                _reconnectionManager.HandleConnectionLost();
            }
            else
            {
                _logger.Log("Connection lost due to server disconnect. Reconnection disabled.");
                State = PlayServState.Offline;
            }
        }

        private void SetupCommandHandlers()
        {
            OnCommand("error", OnCommandErrorReceived);
            OnCommand("Disconnect", OnDisconnectReceived);
            OnCommand("ClientSettingsResponse", OnClientSettingsResponseReceived);
            OnCommand("ParseErrorResponse", OnParseErrorReceived);
            OnCommand("ValidationErrorResponse", OnValidationErrorReceived);
        }

        private void OnDisconnectReceived(object command)
        {
            _logger.LogWarning("Received disconnect command from server.");
            _disconnectedByServer = true;
            _reconnectionManager.Stop();
            State = PlayServState.Offline;
        }

        private void OnClientSettingsResponseReceived(object command)
        {
            if (command is ClientSettingsResponse response)
            {
                _allowMultipleConnections = response.AllowMultipleConnections;
                _logger.Log($"Received client settings response. AllowMultipleConnections = {_allowMultipleConnections}");
            }
            else
            {
                _logger.LogWarning($"Received ClientSettingsResponse with unexpected payload type: {command?.GetType().Name ?? "null"}");
            }
        }

        private void OnParseErrorReceived(object command)
        {
            if (command is ParseErrorResponse response)
            {
                _logger.LogError($"Parse error received from server. Error: {response.Error}, Received JSON: {response.ReceivedJson}");
            }
            else
            {
                _logger.LogWarning($"Received ParseErrorResponse with unexpected payload type: {command?.GetType().Name ?? "null"}");
            }
        }

        private void OnValidationErrorReceived(object command)
        {
            if (command is ValidationErrorResponse response)
            {
                _logger.LogError($"Validation error received from server. Error: {response.Error}, Received JSON: {response.ReceivedJson}");
            }
            else
            {
                _logger.LogWarning($"Received ValidationErrorResponse with unexpected payload type: {command?.GetType().Name ?? "null"}");
            }
        }

        private void OnCommandErrorReceived(object command)
        {
            if (command is CommandErrorResponse response)
            {
                _logger.LogError(
                    $"Server command error received. Error: {response.Error}, Message: {response.Message}, Timestamp: {response.Timestamp}");
            }
            else
            {
                _logger.LogWarning(
                    $"Received 'error' command with unexpected payload type: {command?.GetType().Name ?? "null"}");
            }
        }

        private async Task SendClientSettingsAsync()
        {
            try
            {
                var settings = new ClientSettingsRequest
                {
                    allowMultipleConnections = _allowMultipleConnections
                };

                await _transport.Send(settings, "module_auth");
                _logger.Log($"Client settings sent: AllowMultipleConnections = {_allowMultipleConnections}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to send client settings: {ex.Message}");
            }
        }

        public void Dispose()
        {
            DisposeSpawnManager();

            _keepAliveManager.Stop();
            _keepAliveManager.Dispose();

            _handshakeService.OnError -= HandleTransportError;
            _handshakeService.Dispose();

            _reconnectionManager.Stop();
            _reconnectionManager.Dispose();

            State = PlayServState.Offline;

            if (_transport is Transport transport)
            {
                transport.ConnectionLost -= OnConnectionLost;
            }

            _transport.Dispose();
        }
    }
}
