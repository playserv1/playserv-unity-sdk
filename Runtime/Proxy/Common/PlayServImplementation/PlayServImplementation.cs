using System;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Modules;
using Playserv.Proxy.Implementation;
using Playserv.Proxy.Interfaces;
using Playserv.Proxy.Logging;
using Playserv.Wrapper;

namespace Playserv.Proxy.Common
{
    public sealed class PlayServImplementation : IDisposable, IPlayServRuntimeIdentity
    {
        private readonly ILogger _logger;
        private readonly CoreTransportFacade _transportFacade;
        private readonly PlayServTransportSession _transportSession;
        private readonly PlayServCommandRouter _commandRouter;
        private readonly PlayServModuleHost _moduleHost;

        public PlayServState State => _transportSession.State;

        public string UserId => _transportSession.UserId;

        public IPlayServModuleServiceProvider ModuleServices => _moduleHost.Services;

        public event Action<TransportError> OnTransportError;
        public event Action OnKeepAlivePingSent;
        public event Action OnKeepAlivePongReceived;
        public event Action<string, object> OnModuleCommand;

        public PlayServImplementation(string endpoint)
            : this(endpoint, PlayServJsonCompositionRoot.CreateDefaultSerializer(), new RequestIdGenerator(), PlayServLog.ForCategory(PlayServLogCategory.Transport), registerModules: null) { }

        public PlayServImplementation(string endpoint, Action<PlayServModuleHost> registerModules)
            : this(endpoint, PlayServJsonCompositionRoot.CreateDefaultSerializer(), new RequestIdGenerator(), PlayServLog.ForCategory(PlayServLogCategory.Transport), registerModules: registerModules) { }

        public PlayServImplementation(
            string endpoint,
            PlayServTransportImplementationFactory transportImplementationFactory)
            : this(endpoint, PlayServJsonCompositionRoot.CreateDefaultSerializer(), new RequestIdGenerator(), PlayServLog.ForCategory(PlayServLogCategory.Transport), transportImplementationFactory, registerModules: null) { }

        public PlayServImplementation(
            string endpoint,
            PlayServTransportImplementationFactory transportImplementationFactory,
            Action<PlayServModuleHost> registerModules)
            : this(endpoint, PlayServJsonCompositionRoot.CreateDefaultSerializer(), new RequestIdGenerator(), PlayServLog.ForCategory(PlayServLogCategory.Transport), transportImplementationFactory, registerModules) { }

        private PlayServImplementation(
            string endpoint,
            IMessageSerializer serializer,
            IRequestIdGenerator requestIdGenerator,
            ILogger logger,
            PlayServTransportImplementationFactory transportImplementationFactory = null,
            Action<PlayServModuleHost> registerModules = null)
        {
            var components = PlayServInstanceFactory.Create(
                this,
                endpoint,
                serializer,
                requestIdGenerator,
                logger,
                transportImplementationFactory,
                registerModules,
                SynchronizationContext.Current,
                error => OnTransportError?.Invoke(error),
                () => OnKeepAlivePingSent?.Invoke(),
                () => OnKeepAlivePongReceived?.Invoke(),
                (commandName, command) => OnModuleCommand?.Invoke(commandName, command));

            _logger = components.Logger;
            _moduleHost = components.ModuleHost;
            _transportFacade = new CoreTransportFacade(components.Transport);
            _transportSession = components.TransportSession;
            _commandRouter = new PlayServCommandRouter(_transportFacade, _transportSession, _logger);
        }

        public void SetConfig(
            string gameAccessToken,
            string gameId,
            string userId,
            string gameVersion,
            string sdkVersion = null,
            bool allowMultipleConnections = true,
            int keepAlivePingIntervalMs = 30000,
            int keepAlivePongTimeoutMs = 10000)
            => _transportSession.Configure(
                gameAccessToken,
                gameId,
                userId,
                gameVersion,
                sdkVersion,
                allowMultipleConnections,
                keepAlivePingIntervalMs,
                keepAlivePongTimeoutMs);

        public Task<bool> Connect() => _transportSession.ConnectAsync();

        public IDisposable On<T>(Action<T> onNext) => _transportFacade.On(onNext);

        public void Send<T>(T command, string moduleName = null) => _transportFacade.Send(command, moduleName);

        public Task SendAsync<T>(T command, string moduleName = null) => _transportFacade.SendAsync(command, moduleName);

        public IObservable<T> On<T>() => _transportFacade.On<T>();

        public IObservable<object> OnCommand(string commandName) => _transportFacade.OnCommand(commandName);

        public IDisposable OnCommand(string commandName, Action<object> onNext) => _transportFacade.OnCommand(commandName, onNext);

        public ITransportImplementation GetTransportImplementation() => _transportFacade.GetTransportImplementation();

        internal ITransport GetTransport() => _transportFacade.GetTransport();

        internal ILogger GetLogger() => _logger;

        public bool HasModule(string moduleId) => _moduleHost.HasModule(moduleId);

        internal IPlayServModuleServiceProvider GetModuleServices() => ModuleServices;

        public void Dispose()
        {
            _commandRouter.Dispose();
            _moduleHost.Dispose();
            _transportSession.Dispose();
            _transportFacade.Dispose();
        }
    }
}
