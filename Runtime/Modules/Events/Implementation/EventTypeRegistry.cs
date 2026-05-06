using System;
using System.Collections.Generic;

namespace Playserv.Events
{
    internal sealed class EventTypeRegistry
    {
        private readonly Dictionary<string, Type> _typesByName =
            new Dictionary<string, Type>(StringComparer.Ordinal);
        private readonly HashSet<string> _ambiguousSimpleNames =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly object _lock = new object();

        public static string GetCanonicalName(Type eventType)
        {
            if (eventType == null)
                throw new ArgumentNullException(nameof(eventType));

            return string.IsNullOrWhiteSpace(eventType.FullName)
                ? eventType.Name
                : eventType.FullName;
        }

        public void Register(Type eventType)
        {
            if (eventType == null)
                throw new ArgumentNullException(nameof(eventType));

            lock (_lock)
            {
                RegisterAlias(GetCanonicalName(eventType), eventType);
                RegisterSimpleAlias(eventType.Name, eventType);
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

                if (_ambiguousSimpleNames.Contains(trimmed))
                    return false;

                var lastDotIndex = trimmed.LastIndexOf('.');
                if (lastDotIndex >= 0 && lastDotIndex < trimmed.Length - 1)
                {
                    var simpleName = trimmed.Substring(lastDotIndex + 1);
                    if (_ambiguousSimpleNames.Contains(simpleName))
                        return false;

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

        private void RegisterSimpleAlias(string simpleName, Type eventType)
        {
            if (string.IsNullOrWhiteSpace(simpleName))
                return;

            if (_ambiguousSimpleNames.Contains(simpleName))
                return;

            if (_typesByName.TryGetValue(simpleName, out var existingType) &&
                existingType != eventType)
            {
                _typesByName.Remove(simpleName);
                _ambiguousSimpleNames.Add(simpleName);
                return;
            }

            _typesByName[simpleName] = eventType;
        }
    }
}
