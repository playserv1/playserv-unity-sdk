using System;
using Playserv.Proxy.Interfaces;
using Playserv.Serialization;
using ISdkLogger = Playserv.Proxy.Logging.ILogger;

namespace Playserv.Proxy.Implementation
{
    public sealed class Transport : ITransport
    {
        private readonly ITransportImplementation _implementation;
        private readonly TransportChannelRegistry _channelRegistry;
        private readonly TransportConnectionLifecycle _connectionLifecycle;
        private readonly TransportSender _sender;
        private readonly TransportReceiver _receiver;
        private bool _isDisposed;

        public event EventHandler ConnectionLost;

        public Transport(
            ITransportImplementation implementation,
            IMessageSerializer serializer,
            IRequestIdGenerator requestIdGenerator,
            ISdkLogger logger)
        {
            _implementation = implementation ?? throw new ArgumentNullException(nameof(implementation));
            if (serializer == null)
                throw new ArgumentNullException(nameof(serializer));

            if (logger == null)
                throw new ArgumentNullException(nameof(logger));

            var jsonCodec = ResolveEnvelopeJsonCodec(serializer);
            var envelopeCodec = new MessageEnvelopeCodec(jsonCodec);
            var logRedactor = new TransportLogRedactor(jsonCodec);
            _channelRegistry = new TransportChannelRegistry();
            _receiver = new TransportReceiver(
                serializer,
                envelopeCodec,
                logger,
                _channelRegistry,
                logRedactor);
            _connectionLifecycle = new TransportConnectionLifecycle(
                implementation,
                logger,
                () => _isDisposed,
                _receiver.OnRawNext,
                RaiseConnectionLost);
            _sender = new TransportSender(
                implementation,
                serializer,
                envelopeCodec,
                logger,
                () => _isDisposed,
                logRedactor);
        }

        public System.Threading.Tasks.Task<bool> Connect()
        {
            return _connectionLifecycle.ConnectAsync();
        }

        public void ResetConnection()
        {
            if (_isDisposed)
                return;

            _connectionLifecycle.ResetConnection();
        }

        public System.Threading.Tasks.Task Send<T>(T command, string moduleName = null)
        {
            return _sender.Send(command, moduleName);
        }

        public IObservable<T> OnReceive<T>()
        {
            _connectionLifecycle.EnsureSubscription();
            return _channelRegistry.GetOrCreateTyped<T>();
        }

        public IObservable<object> OnReceive(string commandName)
        {
            if (string.IsNullOrWhiteSpace(commandName))
                throw new ArgumentException("Command name cannot be null or empty.", nameof(commandName));

            _connectionLifecycle.EnsureSubscription();
            return _channelRegistry.GetOrCreateByName(commandName);
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
            _connectionLifecycle.Dispose();
            _implementation.Dispose();
            _channelRegistry.CompleteAndClear();
        }

        private void RaiseConnectionLost()
        {
            ConnectionLost?.Invoke(this, EventArgs.Empty);
        }

        private static IJsonCodec ResolveEnvelopeJsonCodec(IMessageSerializer serializer)
        {
            if (serializer is JsonSerializer jsonSerializer)
                return jsonSerializer.JsonCodec;

            return new NewtonsoftJsonCodec();
        }
    }
}
