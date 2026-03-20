using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Playserv.Proxy.Interfaces;
using ISdkLogger = Playserv.Proxy.Logging.ILogger;

namespace Playserv.Proxy.Implementation
{
    public sealed class Transport : ITransport
    {
        private readonly ITransportImplementation _implementation;
        private readonly IMessageSerializer _serializer;
        private readonly ISdkLogger _logger;
        private readonly object _gate = new object();
        private readonly SemaphoreSlim _sendGate = new SemaphoreSlim(1, 1);
        private readonly Dictionary<Type, ICommandChannel> _channels = new Dictionary<Type, ICommandChannel>();
        private readonly Dictionary<string, ICommandNameChannel> _commandNameChannels = new Dictionary<string, ICommandNameChannel>();
        private IDisposable _rawSubscription;
        private bool _isDisposed;

        public event EventHandler ConnectionLost;

        public Transport(
            ITransportImplementation implementation,
            IMessageSerializer serializer,
            IRequestIdGenerator requestIdGenerator,
            ISdkLogger logger)
        {
            _implementation = implementation ?? throw new ArgumentNullException(nameof(implementation));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<bool> Connect()
        {
            var connected = await _implementation.Connect();
            if (connected)
            {
                EnsureSubscription();
            }
            return connected;
        }

        public void ResetConnection()
        {
            if (_isDisposed)
                return;

            _rawSubscription?.Dispose();
            _rawSubscription = null;
            _implementation.ResetConnection();
        }

        public async Task Send<T>(T command, string moduleName = null)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            if (_isDisposed)
            {
                _logger.LogWarning(
                    $"[Transport] Send skipped because transport is already disposed. Command={typeof(T).Name}");
                return;
            }

            try
            {
                await _sendGate.WaitAsync();
                try
                {
                    if (_isDisposed)
                    {
                        _logger.LogWarning(
                            $"[Transport] Send skipped after wait because transport is disposed. Command={typeof(T).Name}");
                        return;
                    }

                    var envelope = _serializer.Serialize(command, moduleName);
                    var commandJson = JsonConvert.SerializeObject(envelope.Command);
                    var payloadJson = string.IsNullOrWhiteSpace(envelope.Payload)
                        ? "null"
                        : envelope.Payload;
                    var json = $"{{\"Command\":{commandJson},\"Payload\":{payloadJson}}}";
                    var data = Encoding.UTF8.GetBytes(json);

                    await _implementation.Send(data);
                    LogTransportJson($"Message sent: {typeof(T).Name} with payload: {json}", json);
                }
                finally
                {
                    _sendGate.Release();
                }
            }
            catch (ObjectDisposedException) when (_isDisposed)
            {
                // Late send during teardown (for example unsubscribe at PlayMode exit).
                _logger.LogWarning(
                    $"[Transport] Late send ignored during teardown. Command={typeof(T).Name}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to send message {typeof(T).Name}: {ex.Message}");
                throw;
            }
        }

        public IObservable<T> OnReceive<T>()
        {
            EnsureSubscription();

            lock (_gate)
            {
                var type = typeof(T);
                if (_channels.TryGetValue(type, out var existing))
                {
                    return (IObservable<T>)existing;
                }

                var channel = new CommandChannel<T>();
                _channels[type] = channel;
                return channel;
            }
        }

        public IObservable<object> OnReceive(string commandName)
        {
            if (string.IsNullOrWhiteSpace(commandName))
                throw new ArgumentException("Command name cannot be null or empty.", nameof(commandName));
            
            EnsureSubscription();

            lock (_gate)
            {
                if (_commandNameChannels.TryGetValue(commandName, out var existing))
                {
                    return existing;
                }

                var channel = new CommandNameChannel();
                _commandNameChannels[commandName] = channel;
                return channel;
            }
        }

        public ITransportImplementation GetImplementation()
        {
            return _implementation;
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            _rawSubscription?.Dispose();
            _implementation.Dispose();

            lock (_gate)
            {
                foreach (var channel in _channels.Values)
                {
                    channel.Complete();
                }
                _channels.Clear();

                foreach (var channel in _commandNameChannels.Values)
                {
                    channel.Complete();
                }
                _commandNameChannels.Clear();
            }
        }

        private void EnsureSubscription()
        {
            if (_isDisposed)
                return;

            if (_rawSubscription != null)
                return;

            _rawSubscription = _implementation.OnReceive()
                .Subscribe(OnRawNext, OnRawError, OnRawCompleted);
        }

        private void OnRawNext(byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                _logger.LogWarning("Received empty transport frame.");
                return;
            }

            MessageEnvelope envelope;
            object command;
            var json = string.Empty;

            try
            {
                json = Encoding.UTF8.GetString(data);
                LogTransportJson($"Received JSON: {json}", json);
                envelope = MessageEnvelopeParser.Parse(json);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to parse/deserialize incoming frame. Message skipped: {ex.Message}");
                return;
            }

            try
            {
                command = _serializer.Deserialize(envelope);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    $"Failed to deserialize incoming frame for command '{envelope.Command}'. Message skipped: {ex.Message}. Payload: {envelope.Payload}");
                return;
            }

            if (command == null)
            {
                _logger.LogWarning($"Received message with null command. Envelope: {envelope.Command}");
                return;
            }

            ICommandChannel typeChannel = null;
            ICommandNameChannel nameChannel = null;

            lock (_gate)
            {
                _channels.TryGetValue(command.GetType(), out typeChannel);
                if (!string.IsNullOrWhiteSpace(envelope.Command))
                {
                    _commandNameChannels.TryGetValue(envelope.Command, out nameChannel);
                }
            }

            var hasTypedSubscriber = typeChannel != null;
            var hasCommandSubscriber = nameChannel != null;

            if (IsInvokeRpcResponseCommand(envelope.Command))
            {
                _logger.Log(
                    $"[PlayServ][RPC] Incoming command '{envelope.Command}' parsed as '{command.GetType().Name}'. typedSubscriber={hasTypedSubscriber}, nameSubscriber={hasCommandSubscriber}");

                if (!hasTypedSubscriber && !hasCommandSubscriber)
                {
                    _logger.LogWarning(
                        "[PlayServ][RPC] InvokeRpcResponse received but no subscriber is registered. Response will be dropped.");
                }
            }

            try
            {
                typeChannel?.Next(command);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    $"Error delivering message '{envelope.Command}' to typed channel '{command.GetType().Name}': {ex.Message}");
            }

            try
            {
                nameChannel?.Next(command);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    $"Error delivering message '{envelope.Command}' to command-name channel: {ex.Message}");
            }
        }

        private void OnRawError(Exception error)
        {
            _rawSubscription?.Dispose();
            _rawSubscription = null;

            if (_isDisposed)
                return;

            _logger.LogWarning($"[Transport] Raw transport error received. Keeping command channels alive for reconnect. Error={error?.Message}");
            ConnectionLost?.Invoke(this, EventArgs.Empty);
        }

        private void OnRawCompleted()
        {
            _rawSubscription?.Dispose();
            _rawSubscription = null;

            if (_isDisposed)
                return;

            _logger.LogWarning("[Transport] Raw transport completed. Keeping command channels alive for reconnect.");
            ConnectionLost?.Invoke(this, EventArgs.Empty);
        }

        private void CompleteAll()
        {
            ICommandChannel[] typeSnapshot;
            ICommandNameChannel[] nameSnapshot;

            lock (_gate)
            {
                typeSnapshot = new List<ICommandChannel>(_channels.Values).ToArray();
                nameSnapshot = new List<ICommandNameChannel>(_commandNameChannels.Values).ToArray();
                _channels.Clear();
                _commandNameChannels.Clear();
            }

            foreach (var ch in typeSnapshot)
            {
                ch.Complete();
            }

            foreach (var ch in nameSnapshot)
            {
                ch.Complete();
            }
        }

        private void ErrorAll(Exception ex)
        {
            ICommandChannel[] typeSnapshot;
            ICommandNameChannel[] nameSnapshot;

            lock (_gate)
            {
                typeSnapshot = new List<ICommandChannel>(_channels.Values).ToArray();
                nameSnapshot = new List<ICommandNameChannel>(_commandNameChannels.Values).ToArray();
                _channels.Clear();
                _commandNameChannels.Clear();
            }

            foreach (var ch in typeSnapshot)
            {
                ch.Error(ex);
            }

            foreach (var ch in nameSnapshot)
            {
                ch.Error(ex);
            }
        }

        private void LogTransportJson(string message, string json)
        {
#if PlayServ_Logs
            _logger.Log(message);
#else
            if (IsHeartbeatTransportFrame(json))
                return;

            _logger.Log(message);
#endif
        }

        private static bool IsHeartbeatTransportFrame(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return false;

            return json.IndexOf("\"KeepAlive\"", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   json.IndexOf("\"Ping\"", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   json.IndexOf("\"Pong\"", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsInvokeRpcResponseCommand(string commandName)
        {
            if (string.IsNullOrWhiteSpace(commandName))
                return false;

            if (string.Equals(commandName, "InvokeRpcResponse", StringComparison.OrdinalIgnoreCase))
                return true;

            return commandName.EndsWith(".InvokeRpcResponse", StringComparison.OrdinalIgnoreCase);
        }

        private interface ICommandChannel
        {
            void Next(object command);
            void Error(Exception error);
            void Complete();
        }

        private interface ICommandNameChannel : IObservable<object>
        {
            void Next(object command);
            void Error(Exception error);
            void Complete();
        }

        private sealed class CommandChannel<T> : IObservable<T>, ICommandChannel
        {
            private readonly List<IObserver<T>> _observers = new List<IObserver<T>>();
            private readonly object _gate = new object();
            private bool _completed;

            public IDisposable Subscribe(IObserver<T> observer)
            {
                if (observer == null)
                    throw new ArgumentNullException(nameof(observer));

                lock (_gate)
                {
                    if (_completed)
                    {
                        observer.OnCompleted();
                        return new Unsubscriber(_observers, observer, _gate, active: false);
                    }

                    _observers.Add(observer);
                    return new Unsubscriber(_observers, observer, _gate, active: true);
                }
            }

            public void Next(object command) => Next((T)command);

            private void Next(T value)
            {
                IObserver<T>[] snapshot;

                lock (_gate)
                {
                    if (_completed)
                        return;

                    snapshot = _observers.ToArray();
                }

                foreach (var o in snapshot)
                {
                    o.OnNext(value);
                }
            }

            public void Error(Exception error)
            {
                IObserver<T>[] snapshot;

                lock (_gate)
                {
                    if (_completed)
                        return;

                    _completed = true;
                    snapshot = _observers.ToArray();
                    _observers.Clear();
                }

                foreach (var o in snapshot)
                {
                    o.OnError(error);
                }
            }

            public void Complete()
            {
                IObserver<T>[] snapshot;

                lock (_gate)
                {
                    if (_completed)
                        return;

                    _completed = true;
                    snapshot = _observers.ToArray();
                    _observers.Clear();
                }

                foreach (var o in snapshot)
                {
                    o.OnCompleted();
                }
            }

            private sealed class Unsubscriber : IDisposable
            {
                private readonly List<IObserver<T>> _observers;
                private readonly IObserver<T> _observer;
                private readonly object _gate;
                private bool _active;

                public Unsubscriber(List<IObserver<T>> observers, IObserver<T> observer, object gate, bool active)
                {
                    _observers = observers;
                    _observer = observer;
                    _gate = gate;
                    _active = active;
                }

                public void Dispose()
                {
                    if (!_active)
                        return;

                    _active = false;

                    lock (_gate)
                    {
                        _observers.Remove(_observer);
                    }
                }
            }
        }

        private sealed class CommandNameChannel : IObservable<object>, ICommandNameChannel
        {
            private readonly List<IObserver<object>> _observers = new List<IObserver<object>>();
            private readonly object _gate = new object();
            private bool _completed;

            public IDisposable Subscribe(IObserver<object> observer)
            {
                if (observer == null)
                    throw new ArgumentNullException(nameof(observer));

                lock (_gate)
                {
                    if (_completed)
                    {
                        observer.OnCompleted();
                        return new CommandNameUnsubscriber(_observers, observer, _gate, active: false);
                    }

                    _observers.Add(observer);
                    return new CommandNameUnsubscriber(_observers, observer, _gate, active: true);
                }
            }

            public void Next(object command)
            {
                IObserver<object>[] snapshot;

                lock (_gate)
                {
                    if (_completed)
                        return;

                    snapshot = _observers.ToArray();
                }

                foreach (var o in snapshot)
                {
                    o.OnNext(command);
                }
            }

            public void Error(Exception error)
            {
                IObserver<object>[] snapshot;

                lock (_gate)
                {
                    if (_completed)
                        return;

                    _completed = true;
                    snapshot = _observers.ToArray();
                    _observers.Clear();
                }

                foreach (var o in snapshot)
                {
                    o.OnError(error);
                }
            }

            public void Complete()
            {
                IObserver<object>[] snapshot;

                lock (_gate)
                {
                    if (_completed)
                        return;

                    _completed = true;
                    snapshot = _observers.ToArray();
                    _observers.Clear();
                }

                foreach (var o in snapshot)
                {
                    o.OnCompleted();
                }
            }

            private sealed class CommandNameUnsubscriber : IDisposable
            {
                private readonly List<IObserver<object>> _observers;
                private readonly IObserver<object> _observer;
                private readonly object _gate;
                private bool _active;

                public CommandNameUnsubscriber(List<IObserver<object>> observers, IObserver<object> observer, object gate, bool active)
                {
                    _observers = observers;
                    _observer = observer;
                    _gate = gate;
                    _active = active;
                }

                public void Dispose()
                {
                    if (!_active)
                        return;

                    _active = false;

                    lock (_gate)
                    {
                        _observers.Remove(_observer);
                    }
                }
            }
        }
    }
}
