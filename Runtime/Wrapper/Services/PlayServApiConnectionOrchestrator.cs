using System;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Proxy.Common;

namespace Playserv.Wrapper
{
    internal sealed class PlayServApiConnectionOrchestrator
    {
        private readonly object _connectGate = new object();
        private readonly Func<PlayServState> _getState;
        private readonly Func<PlayServSettings> _getOrCreateSettings;
        private readonly Func<PlayServSettings, CancellationToken, Task<PlayServSettings>> _refreshConfiguredGameVersionAsync;
        private readonly Action<PlayServSettings> _applySettings;
        private readonly Func<PlayServImplementation> _getCurrentInstance;
        private readonly Action _disconnect;
        private readonly Action _resetShutdownState;
        private readonly Action<string> _logTrace;
        private readonly Func<bool> _shouldIgnoreMissingInstance;
        private readonly Action<string> _logShutdownIgnoreWarning;
        private Task<bool> _connectTask;

        public PlayServApiConnectionOrchestrator(
            Func<PlayServState> getState,
            Func<PlayServSettings> getOrCreateSettings,
            Func<PlayServSettings, CancellationToken, Task<PlayServSettings>> refreshConfiguredGameVersionAsync,
            Action<PlayServSettings> applySettings,
            Func<PlayServImplementation> getCurrentInstance,
            Action disconnect,
            Action resetShutdownState,
            Action<string> logTrace,
            Func<bool> shouldIgnoreMissingInstance,
            Action<string> logShutdownIgnoreWarning)
        {
            _getState = getState ?? throw new ArgumentNullException(nameof(getState));
            _getOrCreateSettings = getOrCreateSettings ?? throw new ArgumentNullException(nameof(getOrCreateSettings));
            _refreshConfiguredGameVersionAsync = refreshConfiguredGameVersionAsync ?? throw new ArgumentNullException(nameof(refreshConfiguredGameVersionAsync));
            _applySettings = applySettings ?? throw new ArgumentNullException(nameof(applySettings));
            _getCurrentInstance = getCurrentInstance ?? throw new ArgumentNullException(nameof(getCurrentInstance));
            _disconnect = disconnect ?? throw new ArgumentNullException(nameof(disconnect));
            _resetShutdownState = resetShutdownState ?? throw new ArgumentNullException(nameof(resetShutdownState));
            _logTrace = logTrace ?? throw new ArgumentNullException(nameof(logTrace));
            _shouldIgnoreMissingInstance = shouldIgnoreMissingInstance ?? throw new ArgumentNullException(nameof(shouldIgnoreMissingInstance));
            _logShutdownIgnoreWarning = logShutdownIgnoreWarning ?? throw new ArgumentNullException(nameof(logShutdownIgnoreWarning));
        }

        public Task<bool> ConnectAsync()
        {
            lock (_connectGate)
            {
                if (_connectTask != null && !_connectTask.IsCompleted)
                    return _connectTask;

                _connectTask = ConnectInternalAsync();
                return _connectTask;
            }
        }

        public bool TryGetInstanceForFireAndForget(string operationName, out PlayServImplementation instance)
        {
            instance = _getCurrentInstance();
            if (instance != null)
                return true;

            if (_shouldIgnoreMissingInstance())
            {
                _logShutdownIgnoreWarning(operationName);
                return false;
            }

            throw new InvalidOperationException("SDK is not connected. Call Connect() first.");
        }

        private async Task<bool> ConnectInternalAsync()
        {
            try
            {
                var state = _getState();
                if (state == PlayServState.Online ||
                    state == PlayServState.Connecting ||
                    state == PlayServState.Handshaking)
                    throw new InvalidOperationException("PlayServ is already connected or connecting.");

                _resetShutdownState();
                var settings = _getOrCreateSettings();
                _logTrace($"[PlayServ] Connect started. state={state}, gameId={settings.GameId}, endpoint={settings.Endpoint}");
                settings = await _refreshConfiguredGameVersionAsync(settings, CancellationToken.None);
                _logTrace($"[PlayServ] Connect continue after version refresh. resolvedGameVersion={settings.GameVersion}");
                _applySettings(settings);
                _logTrace("[PlayServ] Connect applied settings. Starting transport connect.");

                var instance = _getCurrentInstance()
                    ?? throw new InvalidOperationException("PlayServ instance is not initialized after applying settings.");

                var connected = await instance.Connect();
                _logTrace($"[PlayServ] Connect transport completed. connected={connected}, state={_getState()}");
                if (!connected)
                    _disconnect();

                return connected;
            }
            finally
            {
                lock (_connectGate)
                {
                    if (_connectTask?.IsCompleted == true)
                        _connectTask = null;
                }
            }
        }
    }
}
