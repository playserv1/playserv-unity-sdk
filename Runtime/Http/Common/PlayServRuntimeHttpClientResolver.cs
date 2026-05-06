using System;
using Playserv.Http.Interfaces;
using Playserv.Runtime.Abstractions;

namespace Playserv.Http.Common
{
    public static class PlayServRuntimeHttpClientResolver
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

                _factories = PlayServHttpModuleRegistry.GetFactories();
                return _factories;
            }
        }
    }
}
