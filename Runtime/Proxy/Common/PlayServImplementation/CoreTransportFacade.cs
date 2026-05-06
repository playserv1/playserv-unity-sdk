using System;
using System.Threading.Tasks;
using Playserv.Proxy.Implementation;
using Playserv.Proxy.Interfaces;

namespace Playserv.Proxy.Common
{
    internal sealed class CoreTransportFacade : IDisposable
    {
        private readonly ITransport _transport;

        public CoreTransportFacade(ITransport transport)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        public IDisposable On<T>(Action<T> onNext)
        {
            if (onNext == null)
                throw new ArgumentNullException(nameof(onNext));

            return On<T>().Subscribe(onNext);
        }

        public IObservable<T> On<T>()
        {
            return _transport.OnReceive<T>();
        }

        public void Send<T>(T command, string moduleName = null)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            _ = _transport.Send(command, moduleName);
        }

        public Task SendAsync<T>(T command, string moduleName = null)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            return _transport.Send(command, moduleName);
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

        public ITransportImplementation GetTransportImplementation()
        {
            var transport = _transport as Transport;
            return transport != null ? transport.GetImplementation() : null;
        }

        public ITransport GetTransport()
        {
            return _transport;
        }

        public void Dispose()
        {
            _transport.Dispose();
        }
    }
}
