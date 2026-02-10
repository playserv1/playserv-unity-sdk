using System;
using System.Collections.Generic;
using System.Text;
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
        private readonly Dictionary<Type, ICommandChannel> _channels = new Dictionary<Type, ICommandChannel>();
        private readonly Dictionary<string, ICommandNameChannel> _commandNameChannels = new Dictionary<string, ICommandNameChannel>();
        private IDisposable _rawSubscription;

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

        public async Task Send<T>(T command, string moduleName = null)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            try
            {
                var envelope = _serializer.Serialize(command, moduleName);
                var commandJson = JsonConvert.SerializeObject(envelope.Command);
                var payloadJson = string.IsNullOrWhiteSpace(envelope.Payload)
                    ? "null"
                    : envelope.Payload;
                var json = $"{{\"Command\":{commandJson},\"Payload\":{payloadJson}}}";
                var data = Encoding.UTF8.GetBytes(json);

                await _implementation.Send(data);
                _logger.Log($"Message sent: {typeof(T).Name} with payload: {json}");
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
            if (_rawSubscription != null)
                return;

            _rawSubscription = _implementation.OnReceive()
                .Subscribe(OnRawNext, OnRawError, OnRawCompleted);
        }

        private void OnRawNext(byte[] data)
        {
            try
            {
                var json = Encoding.UTF8.GetString(data);
                _logger.Log($"Received JSON: {json}");
                var envelope = MessageEnvelopeParser.Parse(json);
                var command = _serializer.Deserialize(envelope);

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

                typeChannel?.Next(command);
                nameChannel?.Next(command);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error processing received message: {ex.Message}");
                ErrorAll(ex);
            }
        }

        private void OnRawError(Exception error)
        {
            ErrorAll(error);
            ConnectionLost?.Invoke(this, EventArgs.Empty);
        }

        private void OnRawCompleted()
        {
            CompleteAll();
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
