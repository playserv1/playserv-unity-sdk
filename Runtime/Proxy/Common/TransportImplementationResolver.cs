using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Playserv.Proxy.Implementation;
using Playserv.Proxy.Interfaces;
using Playserv.Proxy.Logging;

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

            var logger = context.Logger ?? new ConsoleLogger();
            var scheme = GetEndpointScheme(endpoint);
            if (string.IsNullOrWhiteSpace(scheme))
                throw new InvalidOperationException($"Transport endpoint '{endpoint}' is not a valid absolute URI.");

            var moduleFactory = ResolveFactory(scheme);
            if (moduleFactory != null)
                return moduleFactory.Create(context);

            if (IsWebSocketCompatibleScheme(scheme))
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return new WebGLWebSocketTransportImplementation(endpoint, logger);
#else
                return new WebSocketTransportImplementation(endpoint, logger);
#endif
            }

            throw new NotSupportedException(
                $"No transport module is registered for scheme '{scheme}'. " +
                "Restore the corresponding transport module folder or use ws:// / wss:// endpoint.");
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

                _factories = DiscoverFactories();
                return _factories;
            }
        }

        private static ITransportModuleFactory[] DiscoverFactories()
        {
            var result = new List<ITransportModuleFactory>();
            var assembly = typeof(TransportImplementationResolver).Assembly;
            var contractType = typeof(ITransportModuleFactory);
            Type[] types;

            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types ?? Array.Empty<Type>();
            }

            foreach (var type in types)
            {
                if (type == null || type.IsAbstract || type.IsInterface)
                    continue;

                if (!contractType.IsAssignableFrom(type))
                    continue;

                if (type.GetConstructor(Type.EmptyTypes) == null)
                    continue;

                if (!(Activator.CreateInstance(type) is ITransportModuleFactory factory))
                    continue;

                result.Add(factory);
            }

            return result
                .OrderBy(x => x.Scheme, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static bool IsWebSocketCompatibleScheme(string scheme)
        {
            return string.Equals(scheme, "ws", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(scheme, "wss", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(scheme, "http", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(scheme, "https", StringComparison.OrdinalIgnoreCase);
        }

        internal static string GetEndpointScheme(string endpoint)
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
                return null;

            return uri.Scheme;
        }
    }
}
