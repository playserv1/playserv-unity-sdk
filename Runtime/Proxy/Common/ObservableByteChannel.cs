using System;
using System.Collections.Generic;
using System.Threading;

namespace Playserv.Proxy.Common
{
    internal sealed class ObservableByteChannel : IObservable<byte[]>
    {
        private readonly List<IObserver<byte[]>> _observers;
        private readonly object _gate;
        private readonly SynchronizationContext _syncContext;
        private bool _completed;

        public ObservableByteChannel(
            List<IObserver<byte[]>> observers,
            object gate,
            SynchronizationContext syncContext)
        {
            _observers = observers ?? throw new ArgumentNullException(nameof(observers));
            _gate = gate ?? throw new ArgumentNullException(nameof(gate));
            _syncContext = syncContext ?? new SynchronizationContext();
        }

        public IDisposable Subscribe(IObserver<byte[]> observer)
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

        public void Next(byte[] value)
        {
            IObserver<byte[]>[] snapshot;

            lock (_gate)
            {
                if (_completed)
                    return;

                snapshot = _observers.ToArray();
            }

            foreach (var observer in snapshot)
                _syncContext.Post(_ => observer.OnNext(value), null);
        }

        public void Error(Exception error)
        {
            IObserver<byte[]>[] snapshot;

            lock (_gate)
            {
                if (_completed)
                    return;

                _completed = true;
                snapshot = _observers.ToArray();
                _observers.Clear();
            }

            foreach (var observer in snapshot)
                _syncContext.Post(_ => observer.OnError(error), null);
        }

        public void Complete()
        {
            IObserver<byte[]>[] snapshot;

            lock (_gate)
            {
                if (_completed)
                    return;

                _completed = true;
                snapshot = _observers.ToArray();
                _observers.Clear();
            }

            foreach (var observer in snapshot)
                _syncContext.Post(_ => observer.OnCompleted(), null);
        }

        public bool IsCompleted
        {
            get
            {
                lock (_gate)
                {
                    return _completed;
                }
            }
        }

        private sealed class Unsubscriber : IDisposable
        {
            private readonly List<IObserver<byte[]>> _observers;
            private readonly IObserver<byte[]> _observer;
            private readonly object _gate;
            private bool _active;

            public Unsubscriber(
                List<IObserver<byte[]>> observers,
                IObserver<byte[]> observer,
                object gate,
                bool active)
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
