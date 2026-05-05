#if !PLAYSERV_DISABLE_RPC_CORE && !PLAYSERV_DISABLE_SERVER_RPC
using System;
using System.Collections.Generic;
using Playserv.Serialization;

namespace Playserv.RPC
{
    /// <summary>
    /// In-process RPC invoker for server/runtime usage without websocket transport.
    /// </summary>
    public sealed class ServerRpcInvoker : IRpcInvoker
    {
        private readonly Dictionary<string, ServerRpcServiceRegistration> _services = new(StringComparer.Ordinal);
        private readonly IJsonCodec _jsonCodec;

        public ServerRpcInvoker()
            : this(new NewtonsoftJsonCodec())
        {
        }

        public ServerRpcInvoker(IJsonCodec jsonCodec)
        {
            _jsonCodec = jsonCodec ?? throw new ArgumentNullException(nameof(jsonCodec));
        }

        /// <summary>
        /// Registers service instance under its runtime type name.
        /// </summary>
        /// <typeparam name="TService">Service type.</typeparam>
        /// <param name="service">Service instance.</param>
        /// <returns>Current invoker instance for chaining.</returns>
        public ServerRpcInvoker RegisterService<TService>(TService service)
            where TService : class
        {
            if (service == null)
                throw new ArgumentNullException(nameof(service));

            return RegisterService(service.GetType().Name, service);
        }

        /// <summary>
        /// Registers service instance under explicit RPC service name.
        /// </summary>
        /// <param name="serviceName">RPC service name.</param>
        /// <param name="service">Service instance.</param>
        /// <returns>Current invoker instance for chaining.</returns>
        public ServerRpcInvoker RegisterService(string serviceName, object service)
        {
            if (string.IsNullOrWhiteSpace(serviceName))
                throw new ArgumentException("Service name is required.", nameof(serviceName));

            if (service == null)
                throw new ArgumentNullException(nameof(service));

            _services[serviceName] = new ServerRpcServiceRegistration(
                service,
                ServerRpcMethodRegistry.For(service.GetType()));
            return this;
        }

        /// <summary>
        /// Removes registered service by name.
        /// </summary>
        /// <param name="serviceName">RPC service name.</param>
        /// <returns>True if service existed and was removed.</returns>
        public bool UnregisterService(string serviceName)
        {
            if (string.IsNullOrWhiteSpace(serviceName))
                throw new ArgumentException("Service name is required.", nameof(serviceName));

            return _services.Remove(serviceName);
        }

        /// <summary>
        /// Clears all registered services.
        /// </summary>
        public void Clear() => _services.Clear();

        /// <summary>
        /// Invokes registered service method by service/method names and base64 payload.
        /// </summary>
        /// <param name="serviceName">RPC service name.</param>
        /// <param name="methodName">RPC method name.</param>
        /// <param name="payloadBase64">Base64-encoded UTF8 JSON payload.</param>
        /// <returns>True when service was found and invocation was executed locally.</returns>
        public bool TryInvoke(string serviceName, string methodName, string payloadBase64)
        {
            if (string.IsNullOrWhiteSpace(serviceName))
                throw new ArgumentException("Service name is required.", nameof(serviceName));

            if (string.IsNullOrWhiteSpace(methodName))
                throw new ArgumentException("Method name is required.", nameof(methodName));

            if (string.IsNullOrWhiteSpace(payloadBase64))
                throw new ArgumentException("Payload base64 is required.", nameof(payloadBase64));

            if (!_services.TryGetValue(serviceName, out var registration))
                return false;

            var method = registration.Methods.GetRequiredMethod(methodName);
            var arguments = method.BuildArguments(payloadBase64, _jsonCodec);
            method.Execute(registration.Service, arguments);
            return true;
        }

        private readonly struct ServerRpcServiceRegistration
        {
            public ServerRpcServiceRegistration(object service, ServerRpcMethodRegistry methods)
            {
                Service = service;
                Methods = methods;
            }

            public object Service { get; }
            public ServerRpcMethodRegistry Methods { get; }
        }
    }
}

#endif
