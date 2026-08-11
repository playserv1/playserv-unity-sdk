using System;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Proxy.Interfaces;
using Playserv.Proxy.Logging;
using Playserv.Runtime.Abstractions;
using Playserv.Wrapper;

namespace Playserv.Proxy.Common
{
    internal sealed class PlayServTransportSession : IDisposable
    {
        private readonly ITransport _transport;
        private readonly ILogger _logger;
        private readonly HandshakeService _handshakeService;
        private readonly RefreshAuthService _refreshAuthService;
        private readonly KeepAliveManager _keepAliveManager;
        private readonly ReconnectionManager _reconnectionManager;
        private readonly SynchronizationContext _mainThreadContext;
        private readonly Action<TransportError> _notifyTransportError;
        private readonly Action _notifyKeepAlivePingSent;
        private readonly Action _notifyKeepAlivePongReceived;
        private readonly Action<string, object> _notifyModuleCommand;
        private readonly Action _onConnected;

        private string _handshakeCredential;
        private string _clientToken;
        private string _authorization;
        private IPlayServRuntimeTokenProvider _runtimeTokenProvider;
        private string _gameId;
        private string _userId;
        private string _gameVersion;
        private string _sdkVersion = SdkInfo.Version;
        private bool _disconnectedByServer;
        private bool _allowMultipleConnections = true;

        public PlayServState State { get; private set; } = PlayServState.Offline;

        public string UserId => _userId ?? string.Empty;

        public PlayServTransportSession(
            ITransport transport,
            ILogger logger,
            SynchronizationContext mainThreadContext,
            Action<TransportError> notifyTransportError,
            Action notifyKeepAlivePingSent,
            Action notifyKeepAlivePongReceived,
            Action<string, object> notifyModuleCommand,
            Action onConnected)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _mainThreadContext = mainThreadContext;
            _notifyTransportError = notifyTransportError ?? (_ => { });
            _notifyKeepAlivePingSent = notifyKeepAlivePingSent ?? (() => { });
            _notifyKeepAlivePongReceived = notifyKeepAlivePongReceived ?? (() => { });
            _notifyModuleCommand = notifyModuleCommand ?? ((_, __) => { });
            _onConnected = onConnected ?? (() => { });

            _handshakeService = new HandshakeService(_transport, _logger);
            _refreshAuthService = new RefreshAuthService(_transport, _logger);
            _keepAliveManager = new KeepAliveManager(_transport, _logger);
            _reconnectionManager = new ReconnectionManager(
                _transport,
                _logger,
                state => State = state,
                IsReconnectEnvironmentReadyAsync,
                ReconnectSessionAsync);

            _handshakeService.OnError += RaiseTransportError;
            _keepAliveManager.OnTimeout += HandleKeepAliveTimeout;
            _keepAliveManager.OnPingSent += () => _notifyKeepAlivePingSent();
            _keepAliveManager.PongReceived += () => _notifyKeepAlivePongReceived();
            _transport.ConnectionLost += OnConnectionLost;
        }

        public void Configure(
            string clientToken,
            string gameId,
            string userId,
            string gameVersion,
            string sdkVersion = null,
            bool allowMultipleConnections = true,
            int keepAlivePingIntervalMs = 30000,
            int keepAlivePongTimeoutMs = 10000,
            IPlayServRuntimeTokenProvider runtimeTokenProvider = null,
            string playerAccessToken = null)
        {
            var normalizedClientToken = PlayServCredentialPolicy.NormalizeClientToken(clientToken);
            var normalizedAuthorization = PlayServCredentialPolicy.NormalizePlayerAuthorization(playerAccessToken);
            if (normalizedClientToken == null && runtimeTokenProvider == null && normalizedAuthorization == null)
            {
                throw new ArgumentException(
                    "A public pk_* client token, runtime token provider, or runtime player access token is required.",
                    nameof(clientToken));
            }

            if (string.IsNullOrWhiteSpace(gameId))
                throw new ArgumentException("Game ID cannot be null or empty.", nameof(gameId));

            if (string.IsNullOrWhiteSpace(userId))
                throw new ArgumentException("User ID cannot be null or empty.", nameof(userId));

            if (string.IsNullOrWhiteSpace(gameVersion))
                throw new ArgumentException("Game version cannot be null or empty.", nameof(gameVersion));

            _clientToken = normalizedClientToken;
            _authorization = normalizedAuthorization;
            _runtimeTokenProvider = runtimeTokenProvider;
            _gameId = gameId;
            _userId = userId;
            _gameVersion = gameVersion;
            _sdkVersion = string.IsNullOrWhiteSpace(sdkVersion) ? SdkInfo.Version : sdkVersion;
            _allowMultipleConnections = allowMultipleConnections;
            _keepAliveManager.PingIntervalMs = keepAlivePingIntervalMs;
            _keepAliveManager.PongTimeoutMs = keepAlivePongTimeoutMs;

            _logger.Log(
                $"Config set: gameId={gameId}, userId={userId}, gameVersion={gameVersion}, sdkVersion={_sdkVersion}, allowMultiple={allowMultipleConnections}, clientTokenSet={!string.IsNullOrEmpty(_clientToken)}, runtimeTokenProviderSet={_runtimeTokenProvider != null}");
        }

        public async Task<bool> ConnectAsync()
        {
            EnsureConfigured();

            _disconnectedByServer = false;
            State = PlayServState.Connecting;
            _logger.Log("Connecting to SDK...");
            try
            {
                await RefreshRuntimeCredentialAsync();
            }
            catch
            {
                State = PlayServState.Offline;
                throw;
            }

            _reconnectionManager.Start();

            var connected = await _transport.Connect();
            if (!connected)
            {
                State = PlayServState.Offline;
                _logger.LogError("Failed to connect to SDK.");
                return false;
            }

            State = PlayServState.Handshaking;
            _logger.Log("Transport connected. Performing handshake...");

            var handshakeResult = await _handshakeService.PerformHandshakeAsync(
                _handshakeCredential,
                _gameId,
                _userId,
                _gameVersion,
                _sdkVersion,
                _clientToken,
                _authorization);
            if (!handshakeResult.Success)
            {
                _logger.LogError($"Handshake failed: {handshakeResult.Error}");
                State = PlayServState.Offline;
                return false;
            }

            await SendClientSettingsAsync();
            _keepAliveManager.Start();
            _onConnected();

            State = PlayServState.Online;
            _logger.Log("SDK connection established successfully. Ready for login.");
            return true;
        }

        public async Task<bool> RefreshPlayerAuthAsync(
            string newAccessToken,
            CancellationToken cancellationToken = default)
        {
            if (State != PlayServState.Online)
                throw new InvalidOperationException("Player auth can only be refreshed while the SDK is online.");

            var authorization = PlayServCredentialPolicy.NormalizePlayerAuthorization(newAccessToken);
            if (authorization == null)
                throw new ArgumentException("Player access token is required.", nameof(newAccessToken));

            var success = await _refreshAuthService.RefreshAsync(authorization, cancellationToken);
            if (success)
            {
                _authorization = authorization;
                _handshakeCredential = ResolveHandshakeCredential(_clientToken, _authorization);
            }

            return success;
        }

        public void HandleDisconnect()
        {
            _logger.LogWarning("Received disconnect command from server.");
            _disconnectedByServer = true;
            StopAndResetConnection();
            State = PlayServState.Offline;
        }

        public void HandleForcedDisconnect(ForcedDisconnectResponse response)
        {
            var rawCode = response?.ErrorCode ?? (int)TransportErrorCode.ForcedDisconnect;
            var code = Enum.IsDefined(typeof(TransportErrorCode), rawCode)
                ? (TransportErrorCode)rawCode
                : TransportErrorCode.ForcedDisconnect;

            var message = string.IsNullOrWhiteSpace(response?.ErrorMessage)
                ? TransportError.FromCode(code).Message
                : response.ErrorMessage;
            var reason = string.IsNullOrWhiteSpace(response?.Reason) ? "Unknown" : response.Reason;
            var serverVersion = string.IsNullOrWhiteSpace(response?.ServerVersion) ? "n/a" : response.ServerVersion;

            _logger.LogWarning(
                $"Received forced disconnect command from server. code={rawCode:D5}, reason={reason}, serverVersion={serverVersion}, message={message}");

            _disconnectedByServer = true;
            StopAndResetConnection();
            State = PlayServState.Offline;

            _notifyTransportError(new TransportError(code, message));
        }

        public void HandleClientSettingsResponse(ClientSettingsResponse response)
        {
            if (response == null)
                throw new ArgumentNullException(nameof(response));

            _allowMultipleConnections = response.AllowMultipleConnections;
            _logger.Log($"Received client settings response. AllowMultipleConnections = {_allowMultipleConnections}");
        }

        public void HandleModuleCommand(string commandName, object command)
        {
            if (string.IsNullOrWhiteSpace(commandName))
                throw new ArgumentException("Command name is required.", nameof(commandName));

            _notifyModuleCommand(commandName, command);
        }

        public void Dispose()
        {
            _keepAliveManager.Stop();
            _keepAliveManager.Dispose();

            _handshakeService.OnError -= RaiseTransportError;
            _handshakeService.Dispose();
            _refreshAuthService.Dispose();

            _reconnectionManager.Stop();
            _reconnectionManager.Dispose();

            State = PlayServState.Offline;
            _transport.ConnectionLost -= OnConnectionLost;
        }

        private async Task<bool> ReconnectSessionAsync()
        {
            _keepAliveManager.Stop();
            _transport.ResetConnection();
            await RefreshRuntimeCredentialAsync();

            var connected = await _transport.Connect();
            if (!connected)
                return false;

            var handshakeResult = await _handshakeService.PerformHandshakeAsync(
                _handshakeCredential,
                _gameId,
                _userId,
                _gameVersion,
                _sdkVersion,
                _clientToken,
                _authorization);
            if (!handshakeResult.Success)
            {
                _logger.LogError($"Reconnection handshake failed: {handshakeResult.Error}");
                _transport.ResetConnection();
                return false;
            }

            await SendClientSettingsAsync();
            _keepAliveManager.Start();
            _onConnected();
            return true;
        }

        private void RaiseTransportError(TransportError error)
        {
            _logger.LogError($"Transport error: {error}");
            _notifyTransportError(error);
        }

        private void HandleKeepAliveTimeout()
        {
            _logger.LogWarning("KeepAlive timeout. Connection may be lost.");
            _keepAliveManager.Stop();
            _reconnectionManager.HandleConnectionLost();
        }

        private void OnConnectionLost(object sender, EventArgs e)
        {
            _logger.LogWarning(
                $"[PlayServ][Connection] Transport connection lost event received. state={State}, disconnectedByServer={_disconnectedByServer}");
            _keepAliveManager.Stop();

            if (!_disconnectedByServer)
            {
                _reconnectionManager.HandleConnectionLost();
                return;
            }

            _logger.Log("Connection lost due to server disconnect. Reconnection disabled.");
            State = PlayServState.Offline;
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

        private void StopAndResetConnection()
        {
            _keepAliveManager.Stop();
            _reconnectionManager.Stop();
            _transport.ResetConnection();
        }

        private void EnsureConfigured()
        {
            if ((_runtimeTokenProvider == null &&
                 string.IsNullOrWhiteSpace(_clientToken) &&
                 string.IsNullOrWhiteSpace(_authorization)) ||
                string.IsNullOrWhiteSpace(_gameId) ||
                string.IsNullOrWhiteSpace(_userId) ||
                string.IsNullOrWhiteSpace(_gameVersion))
            {
                throw new InvalidOperationException("SDK is not configured. Call SetConfig() first.");
            }
        }

        private async Task RefreshRuntimeCredentialAsync()
        {
            if (_runtimeTokenProvider != null)
            {
                _authorization = PlayServCredentialPolicy.NormalizePlayerAuthorization(
                    await _runtimeTokenProvider.GetTokenAsync(CancellationToken.None));
            }

            _handshakeCredential = ResolveHandshakeCredential(_clientToken, _authorization);

            if (string.IsNullOrWhiteSpace(_handshakeCredential))
            {
                throw new InvalidOperationException(
                    "The runtime token provider returned an empty player JWT and no public client token is configured.");
            }
        }

        private static string ResolveHandshakeCredential(
            string clientToken,
            string authorization)
        {
            if (!string.IsNullOrEmpty(clientToken))
                return clientToken;

            return PlayServCredentialPolicy.ExtractBearerToken(authorization);
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

        [Serializable]
        private sealed class ClientSettingsRequest
        {
            public bool allowMultipleConnections;
        }
    }
}
