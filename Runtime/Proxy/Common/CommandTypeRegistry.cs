using System;
using System.Collections.Generic;

namespace Playserv.Proxy.Common
{
    internal static class CommandTypeRegistry
    {
        private static readonly Lazy<IReadOnlyDictionary<string, Type>> TypesByCommandName =
            new Lazy<IReadOnlyDictionary<string, Type>>(BuildRegistry);

        public static bool TryResolve(string commandName, out Type type)
        {
            type = null;
            if (string.IsNullOrWhiteSpace(commandName))
                return false;

            var trimmed = commandName.Trim();
            var normalized = Normalize(trimmed);
            var registry = TypesByCommandName.Value;

            if (registry.TryGetValue(trimmed, out type))
                return true;

            if (!string.Equals(normalized, trimmed, StringComparison.Ordinal) &&
                registry.TryGetValue(normalized, out type))
            {
                return true;
            }

            if (normalized.EndsWith("+Error", StringComparison.OrdinalIgnoreCase))
            {
                type = typeof(CommandErrorResponse);
                return true;
            }

            return false;
        }

        private static IReadOnlyDictionary<string, Type> BuildRegistry()
        {
            var builder = new CommandTypeRegistryBuilder();
            ProxyCommandTypeRegistration.Register(builder);
#if !PLAYSERV_DISABLE_EVENTS
            EventCommandTypeRegistration.Register(builder);
#endif
#if !PLAYSERV_DISABLE_DATA && !PLAYSERV_DISABLE_EVENTS
            DataSubscriptionCommandTypeRegistration.Register(builder);
#endif
#if !PLAYSERV_DISABLE_RPC_CORE && !PLAYSERV_DISABLE_CLIENT_RPC
            RpcCommandTypeRegistration.Register(builder);
#endif
            return builder.Build();
        }

        private static string Normalize(string commandName)
        {
            if (string.IsNullOrWhiteSpace(commandName))
                return string.Empty;

            var trimmed = commandName.Trim();
            var dotIndex = trimmed.LastIndexOf('.');
            if (dotIndex >= 0 && dotIndex < trimmed.Length - 1)
                return trimmed.Substring(dotIndex + 1);

            return trimmed;
        }
    }
}
