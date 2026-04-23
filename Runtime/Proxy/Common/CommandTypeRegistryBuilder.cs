using System;
using System.Collections.Generic;

namespace Playserv.Proxy.Common
{
    internal sealed class CommandTypeRegistryBuilder
    {
        private readonly Dictionary<string, Type> _typesByCommandName =
            new Dictionary<string, Type>(StringComparer.Ordinal);

        public void Register<T>(params string[] aliases)
        {
            Register(typeof(T), aliases);
        }

        public void Register(Type type, params string[] aliases)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            RegisterAlias(type.Name, type);

            if (aliases == null)
                return;

            for (var i = 0; i < aliases.Length; i++)
            {
                RegisterAlias(aliases[i], type);
            }
        }

        public IReadOnlyDictionary<string, Type> Build()
        {
            return _typesByCommandName;
        }

        private void RegisterAlias(string commandName, Type type)
        {
            if (string.IsNullOrWhiteSpace(commandName))
                return;

            var normalized = Normalize(commandName);
            if (string.IsNullOrWhiteSpace(normalized))
                return;

            _typesByCommandName[normalized] = type;
        }

        private static string Normalize(string commandName)
        {
            return commandName == null ? string.Empty : commandName.Trim();
        }
    }
}
