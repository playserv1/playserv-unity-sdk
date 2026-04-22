using System;
using System.Collections.Generic;
using System.Reflection;
using Playserv.Http.Interfaces;

namespace Playserv.Http.Common
{
    internal static class PlayServRuntimeHttpClientResolver
    {
        private static readonly object Gate = new object();
        private static IPlayServHttpModuleFactory[] _factories;

        public static IPlayServRuntimeHttpClient Create(PlayServHttpModuleContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            foreach (var factory in GetFactories())
            {
                var client = factory.Create(context);
                if (client != null)
                    return client;
            }

            throw new NotSupportedException(
                "No HTTP module is registered for PlayServ runtime. Restore Runtime/Http/Modules or disable HTTP-backed runtime features.");
        }

        private static IPlayServHttpModuleFactory[] GetFactories()
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

        private static IPlayServHttpModuleFactory[] DiscoverFactories()
        {
            var result = new List<IPlayServHttpModuleFactory>();
            var assembly = typeof(PlayServRuntimeHttpClientResolver).Assembly;
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

                if (!typeof(IPlayServHttpModuleFactory).IsAssignableFrom(type))
                    continue;

                if (type.GetConstructor(Type.EmptyTypes) == null)
                    continue;

                if (Activator.CreateInstance(type) is IPlayServHttpModuleFactory factory)
                    result.Add(factory);
            }

            return result.ToArray();
        }
    }
}
