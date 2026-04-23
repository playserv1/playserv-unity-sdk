using System;
using System.Collections.Generic;

namespace Playserv.Events
{
    internal sealed class EventTypeRegistry
    {
        private readonly Dictionary<string, Type> _typesByName =
            new Dictionary<string, Type>(StringComparer.Ordinal);
        private readonly object _lock = new object();

        public void Register(Type eventType)
        {
            if (eventType == null)
                throw new ArgumentNullException(nameof(eventType));

            lock (_lock)
            {
                RegisterAlias(eventType.Name, eventType);

                if (!string.IsNullOrWhiteSpace(eventType.FullName))
                    RegisterAlias(eventType.FullName, eventType);
            }
        }

        public bool TryResolve(string eventTypeName, out Type eventType)
        {
            eventType = null;
            if (string.IsNullOrWhiteSpace(eventTypeName))
                return false;

            var trimmed = eventTypeName.Trim();
            lock (_lock)
            {
                if (_typesByName.TryGetValue(trimmed, out eventType))
                    return true;

                var lastDotIndex = trimmed.LastIndexOf('.');
                if (lastDotIndex >= 0 && lastDotIndex < trimmed.Length - 1)
                {
                    var simpleName = trimmed.Substring(lastDotIndex + 1);
                    if (_typesByName.TryGetValue(simpleName, out eventType))
                        return true;
                }
            }

            return false;
        }

        private void RegisterAlias(string alias, Type eventType)
        {
            if (string.IsNullOrWhiteSpace(alias))
                return;

            _typesByName[alias] = eventType;
        }
    }
}
