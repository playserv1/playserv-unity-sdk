using System;
using System.Collections.Generic;

namespace Playserv.Proxy.Common
{
    internal static class CommandTypeRegistry
    {
        private static readonly object Sync = new object();
        private static IReadOnlyDictionary<string, Type> _typesByCommandName;

        public static bool TryResolve(string commandName, out Type type)
        {
            type = null;
            if (string.IsNullOrWhiteSpace(commandName))
                return false;

            var trimmed = commandName.Trim();
            var normalized = Normalize(trimmed);
            var registry = GetRegistry();

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

        internal static void Invalidate()
        {
            lock (Sync)
                _typesByCommandName = null;
        }

        private static IReadOnlyDictionary<string, Type> GetRegistry()
        {
            lock (Sync)
            {
                if (_typesByCommandName == null)
                    _typesByCommandName = BuildRegistry();

                return _typesByCommandName;
            }
        }

        private static IReadOnlyDictionary<string, Type> BuildRegistry()
        {
            var builder = new CommandTypeRegistryBuilder();
            var providers = CommandTypeProviderRegistry.Snapshot();
            for (var i = 0; i < providers.Length; i++)
                providers[i].RegisterCommandTypes(builder);

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
