using System;
using System.Threading;
using Playserv.Modules;
using Playserv.Proxy.Implementation;
using Playserv.Proxy.Interfaces;
using Playserv.Proxy.Logging;
#if !PLAYSERV_DISABLE_RPC_CORE && !PLAYSERV_DISABLE_CLIENT_RPC
using Playserv.RPC;
#endif
using Playserv.Runtime.Abstractions;
using Playserv.Serialization;

namespace Playserv.Proxy.Common
{
    internal static class PlayServInstanceFactory
    {
        public static PlayServImplementationComponents Create(
            PlayServImplementation owner,
            string endpoint,
            IMessageSerializer serializer,
            IRequestIdGenerator requestIdGenerator,
            ILogger logger,
            PlayServTransportImplementationFactory transportImplementationFactory,
            SynchronizationContext mainThreadContext,
            Action<TransportError> notifyTransportError,
            Action notifyKeepAlivePingSent,
            Action notifyKeepAlivePongReceived
#if !PLAYSERV_DISABLE_RPC_CORE && !PLAYSERV_DISABLE_CLIENT_RPC
            ,
            Action<InvokeRpcResponse> notifyRpcInvokeResponse)
#else
            )
#endif
        {
            if (owner == null)
                throw new ArgumentNullException(nameof(owner));

            if (serializer == null)
                throw new ArgumentNullException(nameof(serializer));

            if (requestIdGenerator == null)
                throw new ArgumentNullException(nameof(requestIdGenerator));

            if (logger == null)
                throw new ArgumentNullException(nameof(logger));

            if (string.IsNullOrWhiteSpace(endpoint))
                throw new ArgumentException("Endpoint cannot be null or empty.", nameof(endpoint));

            var jsonComposition = PlayServJsonCompositionRoot.Resolve(serializer);
            serializer = jsonComposition.Serializer;
            var jsonCodec = jsonComposition.JsonCodec;

            if (transportImplementationFactory == null)
                transportImplementationFactory = CreateDefaultTransportImplementationFactory(logger);

            var implementation = transportImplementationFactory(endpoint, jsonCodec);
            if (implementation == null)
                throw new InvalidOperationException("Transport implementation factory returned null.");

            var transport = new Transport(implementation, serializer, requestIdGenerator, logger);
            var moduleHost = CreateModuleHost(
                owner,
                transport,
                serializer,
                requestIdGenerator,
                logger,
                jsonCodec);
            var transportSession = new PlayServTransportSession(
                transport,
                logger,
                mainThreadContext,
                notifyTransportError,
                notifyKeepAlivePingSent,
                notifyKeepAlivePongReceived,
#if !PLAYSERV_DISABLE_RPC_CORE && !PLAYSERV_DISABLE_CLIENT_RPC
                notifyRpcInvokeResponse,
#endif
                moduleHost.NotifyConnected);

            return new PlayServImplementationComponents(
                transport,
                logger,
                moduleHost,
                transportSession);
        }

        private static PlayServTransportImplementationFactory CreateDefaultTransportImplementationFactory(ILogger logger)
        {
            return (endpoint, jsonCodec) => TransportImplementationResolver.Create(
                new TransportModuleContext(endpoint, logger, jsonCodec: jsonCodec));
        }

        private static PlayServModuleHost CreateModuleHost(
            PlayServImplementation owner,
            ITransport transport,
            IMessageSerializer serializer,
            IRequestIdGenerator requestIdGenerator,
            ILogger logger,
            IJsonCodec jsonCodec)
        {
            var moduleHost = new PlayServModuleHost();
            var services = moduleHost.ServiceRegistry;
            services.Register(owner);
            services.Register<IPlayServRuntimeIdentity>(owner);
            services.Register(transport);
            services.Register(serializer);
            services.Register(requestIdGenerator);
            services.Register(logger);
            services.Register(jsonCodec);

            PlayServModuleRegistry.RegisterDefaults(moduleHost);

            moduleHost.Initialize(new PlayServModuleContext(services));
            return moduleHost;
        }
    }
}
