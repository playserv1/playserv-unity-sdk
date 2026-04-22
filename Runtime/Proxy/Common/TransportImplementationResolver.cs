using System;
using Playserv.Proxy.Interfaces;
using Playserv.Runtime.Abstractions;

namespace Playserv.Proxy.Common
{
    internal static class TransportImplementationResolver
    {
        private static readonly object Gate = new object();
        private static ITransportModuleFactory[] _factories;

        public static ITransportImplementation Create(TransportModuleContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            var endpoint = context.Endpoint?.Trim();
            if (string.IsNullOrWhiteSpace(endpoint))
                throw new ArgumentException("Transport endpoint cannot be null or empty.", nameof(context));

            var scheme = GetEndpointScheme(endpoint);
            if (string.IsNullOrWhiteSpace(scheme))
                throw new InvalidOperationException($"Transport endpoint '{endpoint}' is not a valid absolute URI.");

            var moduleFactory = ResolveFactory(scheme);
            if (moduleFactory != null)
                return moduleFactory.Create(context);

            throw new NotSupportedException(
                $"No transport module is registered for scheme '{scheme}'. " +
                "Restore the corresponding transport module folder.");
        }

        public static bool HasModuleForEndpoint(string endpoint)
        {
            var scheme = GetEndpointScheme(endpoint);
            return !string.IsNullOrWhiteSpace(scheme) && ResolveFactory(scheme) != null;
        }

        private static ITransportModuleFactory ResolveFactory(string scheme)
        {
            if (string.IsNullOrWhiteSpace(scheme))
                return null;

            foreach (var factory in GetFactories())
            {
                if (string.Equals(factory.Scheme, scheme, StringComparison.OrdinalIgnoreCase))
                    return factory;
            }

            return null;
        }

        private static ITransportModuleFactory[] GetFactories()
        {
            if (_factories != null)
                return _factories;

            lock (Gate)
            {
                if (_factories != null)
                    return _factories;

                _factories = TransportModuleRegistry.GetFactories();
                return _factories;
            }
        }

        internal static string GetEndpointScheme(string endpoint)
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
                return null;

            return uri.Scheme;
        }
    }
}
