using System;
using System.Collections.Generic;

namespace Playserv.Proxy.Implementation
{
    internal interface ICommandChannel
    {
        void Next(object command);
        void Error(Exception error);
        void Complete();
    }

    internal interface ICommandNameChannel : IObservable<object>
    {
        void Next(object command);
        void Error(Exception error);
        void Complete();
    }

    internal sealed class TransportChannelRegistry
    {
        private readonly object _gate = new object();
        private readonly Dictionary<Type, ICommandChannel> _channels = new Dictionary<Type, ICommandChannel>();
        private readonly Dictionary<string, ICommandNameChannel> _commandNameChannels = new Dictionary<string, ICommandNameChannel>();

        public IObservable<T> GetOrCreateTyped<T>()
        {
            lock (_gate)
            {
                var type = typeof(T);
                if (_channels.TryGetValue(type, out var existing))
                    return (IObservable<T>)existing;

                var channel = new CommandChannel<T>();
                _channels[type] = channel;
                return channel;
            }
        }

        public IObservable<object> GetOrCreateByName(string commandName)
        {
            lock (_gate)
            {
                if (_commandNameChannels.TryGetValue(commandName, out var existing))
                    return existing;

                var channel = new CommandNameChannel();
                _commandNameChannels[commandName] = channel;
                return channel;
            }
        }

        public void TryGetChannels(Type type, string commandName, out ICommandChannel typeChannel, out ICommandNameChannel nameChannel)
        {
            lock (_gate)
            {
                _channels.TryGetValue(type, out typeChannel);
                nameChannel = null;
                if (!string.IsNullOrWhiteSpace(commandName))
                    _commandNameChannels.TryGetValue(commandName, out nameChannel);
            }
        }

        public void CompleteAndClear()
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

        public void ErrorAndClear(Exception error)
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
                ch.Error(error);
            }

            foreach (var ch in nameSnapshot)
            {
                ch.Error(error);
            }
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
