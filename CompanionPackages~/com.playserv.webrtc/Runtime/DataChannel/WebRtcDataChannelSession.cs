using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Proxy.Common;
using Playserv.Proxy.Logging;

namespace Playserv.Proxy.Implementation
{
    internal sealed class WebRtcDataChannelSession
    {
        private readonly object _gate = new object();
        private readonly object _connectGate = new object();
        private readonly List<IObserver<byte[]>> _observers = new List<IObserver<byte[]>>();
        private readonly SynchronizationContext _syncContext;

        private ObservableByteChannel _channel;
        private TaskCompletionSource<bool> _connectTcs;
        private bool _connecting;
        private bool _isConnected;
        private bool _isDisposed;

        public WebRtcDataChannelSession(SynchronizationContext syncContext)
        {
            _syncContext = syncContext ?? new SynchronizationContext();
            _channel = new ObservableByteChannel(_observers, _gate, _syncContext);
        }

        public bool IsDisposed => _isDisposed;

        public bool IsConnected
        {
            get
            {
                lock (_connectGate)
                    return _isConnected;
            }
        }

        public IObservable<byte[]> OnReceive()
        {
            return _channel;
        }

        public void ThrowIfDisposed(string objectName)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(objectName);
        }

        public bool BeginConnect(
            ILogger logger,
            out Task<bool> existingConnectTask,
            out TaskCompletionSource<bool> connectTcs,
            out bool alreadyConnected)
        {
            existingConnectTask = null;
            connectTcs = null;
            alreadyConnected = false;

            lock (_connectGate)
            {
                if (_connecting)
                {
                    logger?.LogWarning("WebRTC DataChannel connect already in progress.");
                    existingConnectTask = _connectTcs?.Task ?? Task.FromResult(false);
                    return false;
                }

                if (_isConnected)
                {
                    if (_channel.IsCompleted)
                    {
                        logger?.LogWarning("WebRTC DataChannel is marked connected but channel is completed. Recreating receive channel.");
                        _isConnected = false;
                        ResetChannel();
                    }
                    else
                    {
                        logger?.LogWarning("WebRTC DataChannel is already connected.");
                        alreadyConnected = true;
                        return false;
                    }
                }
                else if (_channel.IsCompleted)
                {
                    ResetChannel();
                }

                _connecting = true;
                _isConnected = false;
                _connectTcs = new TaskCompletionSource<bool>();
                connectTcs = _connectTcs;
                return true;
            }
        }

        public bool TryCompleteConnect(out TaskCompletionSource<bool> pendingConnect)
        {
            lock (_connectGate)
            {
                if (!_connecting)
                {
                    pendingConnect = null;
                    return false;
                }

                pendingConnect = _connectTcs;
                _connectTcs = null;
                _connecting = false;
                _isConnected = true;
                return true;
            }
        }

        public bool TryDeactivate(out TaskCompletionSource<bool> pendingConnect, out bool wasConnected, bool ignoreIfInactive)
        {
            lock (_connectGate)
            {
                if (ignoreIfInactive && !_connecting && !_isConnected)
                {
                    pendingConnect = null;
                    wasConnected = false;
                    return false;
                }

                wasConnected = _isConnected;
                pendingConnect = _connectTcs;
                _connectTcs = null;
                _connecting = false;
                _isConnected = false;
                return true;
            }
        }

        public void FailPendingConnect(TaskCompletionSource<bool> connectTcs, bool completeChannel)
        {
            lock (_connectGate)
            {
                if (ReferenceEquals(_connectTcs, connectTcs))
                    _connectTcs = null;

                _connecting = false;
                _isConnected = false;
            }

            connectTcs?.TrySetResult(false);
            if (completeChannel)
                Complete();
        }

        public bool TryTimeout(TaskCompletionSource<bool> connectTcs)
        {
            lock (_connectGate)
            {
                if (!ReferenceEquals(_connectTcs, connectTcs) || !_connecting)
                    return false;

                _connecting = false;
                _isConnected = false;
                _connectTcs = null;
                return true;
            }
        }

        public void Reset(out TaskCompletionSource<bool> pendingConnect)
        {
            lock (_connectGate)
            {
                pendingConnect = _connectTcs;
                _connectTcs = null;
                _connecting = false;
                _isConnected = false;
            }

            ResetChannel();
        }

        public void MarkDisposed()
        {
            _isDisposed = true;
            lock (_connectGate)
            {
                _connecting = false;
                _isConnected = false;
                _connectTcs = null;
            }
        }

        public void Next(byte[] data)
        {
            _channel.Next(data);
        }

        public void Complete()
        {
            _channel.Complete();
        }

        private void ResetChannel()
        {
            lock (_gate)
            {
                _observers.Clear();
                _channel = new ObservableByteChannel(_observers, _gate, _syncContext);
            }
        }
    }
}
