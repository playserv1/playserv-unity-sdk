using System;
using System.Collections.Generic;
using Playserv.Serialization;

namespace Playserv.DebugTerminal
{
    /// <summary>Small strict option parser shared by the runtime smoke-test commands.</summary>
    internal sealed class DebugTerminalArguments
    {
        private static readonly IJsonCodec Json = new NewtonsoftJsonCodec();
        private readonly Dictionary<string, List<string>> _options;

        private DebugTerminalArguments(
            Dictionary<string, List<string>> options,
            List<string> positionals)
        {
            _options = options;
            Positionals = positionals;
        }

        internal IReadOnlyList<string> Positionals { get; }

        internal static bool TryParse(
            IReadOnlyList<string> parts,
            int start,
            IReadOnlyCollection<string> valueOptions,
            IReadOnlyCollection<string> flags,
            out DebugTerminalArguments arguments,
            out string error)
        {
            arguments = null;
            error = null;
            var options = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var positionals = new List<string>();
            var values = new HashSet<string>(valueOptions ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var switches = new HashSet<string>(flags ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

            for (var index = start; index < (parts?.Count ?? 0); index++)
            {
                var token = parts[index] ?? string.Empty;
                if (!token.StartsWith("--", StringComparison.Ordinal))
                {
                    positionals.Add(token);
                    continue;
                }

                var name = token.Substring(2);
                if (switches.Contains(name))
                {
                    Add(options, name, "true");
                    continue;
                }

                if (!values.Contains(name))
                {
                    error = $"Unknown option '--{name}'.";
                    return false;
                }

                if (++index >= parts.Count || (parts[index] ?? string.Empty).StartsWith("--", StringComparison.Ordinal))
                {
                    error = $"Option '--{name}' requires a value.";
                    return false;
                }

                Add(options, name, parts[index]);
            }

            arguments = new DebugTerminalArguments(options, positionals);
            return true;
        }

        internal bool Has(string name) => _options.ContainsKey(name);

        internal string Get(string name, string fallback = null)
        {
            return _options.TryGetValue(name, out var values) && values.Count > 0
                ? values[values.Count - 1]
                : fallback;
        }

        internal IReadOnlyList<string> GetAll(string name)
        {
            return _options.TryGetValue(name, out var values)
                ? values
                : Array.Empty<string>();
        }

        internal bool TryGetInt(
            string name,
            int fallback,
            int minimum,
            int maximum,
            out int value,
            out string error)
        {
            error = null;
            var text = Get(name);
            if (text == null)
            {
                value = fallback;
                return true;
            }

            if (!int.TryParse(text, out value) || value < minimum || value > maximum)
            {
                error = $"Option '--{name}' must be between {minimum} and {maximum}.";
                return false;
            }

            return true;
        }

        internal bool TryGetLong(
            string name,
            long fallback,
            long minimum,
            long maximum,
            out long value,
            out string error)
        {
            error = null;
            var text = Get(name);
            if (text == null)
            {
                value = fallback;
                return true;
            }

            if (!long.TryParse(text, out value) || value < minimum || value > maximum)
            {
                error = $"Option '--{name}' must be between {minimum} and {maximum}.";
                return false;
            }

            return true;
        }

        internal static bool TryParsePairs(
            IEnumerable<string> values,
            out Dictionary<string, string> pairs,
            out string error)
        {
            pairs = new Dictionary<string, string>(StringComparer.Ordinal);
            error = null;
            foreach (var value in values ?? Array.Empty<string>())
            {
                var separator = value?.IndexOf('=') ?? -1;
                if (separator <= 0)
                {
                    error = $"Expected key=value, received '{value}'.";
                    return false;
                }

                pairs[value.Substring(0, separator)] = value.Substring(separator + 1);
            }
            return true;
        }

        internal static bool TryParseJson(
            string text,
            bool requireObject,
            out object value,
            out string error)
        {
            value = null;
            error = null;
            if (string.IsNullOrWhiteSpace(text))
                return true;

            try
            {
                value = Json.ParseToPlainValue(text);
            }
            catch (JsonCodecException exception)
            {
                error = $"Invalid JSON: {exception.Message}";
                return false;
            }

            if (requireObject && !(value is IDictionary<string, object>))
            {
                error = "JSON value must be an object.";
                value = null;
                return false;
            }

            return true;
        }

        internal static object ParseLooseValue(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text ?? string.Empty;
            return TryParseJson(text, false, out var value, out _) ? value : text;
        }

        private static void Add(
            IDictionary<string, List<string>> options,
            string name,
            string value)
        {
            if (!options.TryGetValue(name, out var values))
            {
                values = new List<string>();
                options[name] = values;
            }
            values.Add(value);
        }
    }
}
