using System;
using System.Threading.Tasks;
using Playserv.Proxy.Interfaces;
using ISdkLogger = Playserv.Proxy.Logging.ILogger;

namespace Playserv.Proxy.Implementation
{
    internal sealed class TransportConnectionLifecycle : IDisposable
    {
        private readonly ITransportImplementation _implementation;
        private readonly ISdkLogger _logger;
        private readonly Func<bool> _isDisposed;
        private readonly Action<byte[]> _onRawNext;
        private readonly Action _raiseConnectionLost;
        private readonly object _gate = new object();
        private IDisposable _rawSubscription;

        public TransportConnectionLifecycle(
            ITransportImplementation implementation,
            ISdkLogger logger,
            Func<bool> isDisposed,
            Action<byte[]> onRawNext,
            Action raiseConnectionLost)
        {
            _implementation = implementation ?? throw new ArgumentNullException(nameof(implementation));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _isDisposed = isDisposed ?? throw new ArgumentNullException(nameof(isDisposed));
            _onRawNext = onRawNext ?? throw new ArgumentNullException(nameof(onRawNext));
            _raiseConnectionLost = raiseConnectionLost ?? throw new ArgumentNullException(nameof(raiseConnectionLost));
        }

        public async Task<bool> ConnectAsync()
        {
            var connected = await _implementation.Connect();
            if (connected)
                EnsureSubscription();

            return connected;
        }

        public void ResetConnection()
        {
            if (_isDisposed())
                return;

            DisposeSubscription();
            _implementation.ResetConnection();
        }

        public void EnsureSubscription()
        {
            if (_isDisposed())
                return;

            lock (_gate)
            {
                if (_rawSubscription != null)
                    return;

                _rawSubscription = _implementation.OnReceive()
                    .Subscribe(_onRawNext, OnRawError, OnRawCompleted);
            }
        }

        private void OnRawError(Exception error)
        {
            DisposeSubscription();

            if (_isDisposed())
                return;

            _logger.LogWarning($"[Transport] Raw transport error received. Keeping command channels alive for reconnect. Error={error?.Message}");
            _raiseConnectionLost();
        }

        private void OnRawCompleted()
        {
            DisposeSubscription();

            if (_isDisposed())
                return;

            _logger.LogWarning("[Transport] Raw transport completed. Keeping command channels alive for reconnect.");
            _raiseConnectionLost();
        }

        public void Dispose()
        {
            DisposeSubscription();
        }

        private void DisposeSubscription()
        {
            IDisposable subscription = null;
            lock (_gate)
            {
                subscription = _rawSubscription;
                _rawSubscription = null;
            }

            subscription?.Dispose();
        }
    }
}
