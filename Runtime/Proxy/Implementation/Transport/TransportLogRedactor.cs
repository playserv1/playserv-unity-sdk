using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Playserv.Serialization;

namespace Playserv.Proxy.Implementation
{
    internal sealed class TransportLogRedactor
    {
        internal const string RedactedValue = "[REDACTED]";

        private const int MaxDepth = 16;

        private static readonly Regex CredentialPrefix = new Regex(
            "(?<![A-Za-z0-9_])(?:pk|sk)_[A-Za-z0-9._~-]+",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex BearerValue = new Regex(
            "\\bBearer\\s+[^\\s,;\\\"]+",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex JwtValue = new Regex(
            "(?<![A-Za-z0-9_-])(?=[A-Za-z0-9_-]*[A-Za-z])[A-Za-z0-9_-]+\\.(?=[A-Za-z0-9_-]*[A-Za-z])[A-Za-z0-9_-]+\\.[A-Za-z0-9_-]+(?![A-Za-z0-9_-])",
            RegexOptions.CultureInvariant);

        private static readonly Regex SensitiveJsonValue = new Regex(
            "(\\\"(?:game[-_]?access[-_]?token|client[-_]?token|authorization)\\\"\\s*:\\s*)\\\"(?:\\\\.|[^\\\"])*\\\"",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex SensitiveAssignment = new Regex(
            "\\b(game[-_]?access[-_]?token|client[-_]?token|authorization)\\b\\s*[=:]\\s*[^&\\s,;\\\"}]+",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private readonly IJsonCodec _jsonCodec;

        public TransportLogRedactor(IJsonCodec jsonCodec)
        {
            _jsonCodec = jsonCodec ?? throw new ArgumentNullException(nameof(jsonCodec));
        }

        public string RedactJson(string json)
        {
            if (string.IsNullOrEmpty(json))
                return json ?? string.Empty;

            try
            {
                var value = _jsonCodec.ParseToPlainValue(json);
                var redacted = RedactValue(value, null, 0);
                return _jsonCodec.Serialize(redacted);
            }
            catch
            {
                return RedactedValue;
            }
        }

        public string RedactText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text ?? string.Empty;

            try
            {
                var sanitized = SensitiveJsonValue.Replace(text, "$1\"[REDACTED]\"");
                sanitized = BearerValue.Replace(sanitized, RedactedValue);
                sanitized = SensitiveAssignment.Replace(sanitized, "$1=[REDACTED]");
                sanitized = CredentialPrefix.Replace(sanitized, RedactedValue);
                return JwtValue.Replace(sanitized, RedactedValue);
            }
            catch
            {
                return RedactedValue;
            }
        }

        private object RedactValue(object value, string propertyName, int depth)
        {
            if (IsSensitiveProperty(propertyName) || depth > MaxDepth)
                return RedactedValue;

            if (value == null)
                return null;

            if (value is string text)
                return RedactString(text, depth);

            if (value is IDictionary<string, object> dictionary)
            {
                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var pair in dictionary)
                    result[pair.Key] = RedactValue(pair.Value, pair.Key, depth + 1);

                return result;
            }

            if (value is IDictionary nonGenericDictionary)
            {
                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (DictionaryEntry entry in nonGenericDictionary)
                {
                    var key = entry.Key?.ToString() ?? string.Empty;
                    result[key] = RedactValue(entry.Value, key, depth + 1);
                }

                return result;
            }

            if (value is IList<object> list)
            {
                var result = new List<object>(list.Count);
                for (var index = 0; index < list.Count; index++)
                    result.Add(RedactValue(list[index], null, depth + 1));

                return result;
            }

            if (value is IList nonGenericList && value is not byte[])
            {
                var result = new List<object>(nonGenericList.Count);
                for (var index = 0; index < nonGenericList.Count; index++)
                    result.Add(RedactValue(nonGenericList[index], null, depth + 1));

                return result;
            }

            return value;
        }

        private string RedactString(string value, int depth)
        {
            if (depth <= MaxDepth && LooksLikeJson(value))
            {
                try
                {
                    var nested = _jsonCodec.ParseToPlainValue(value);
                    var redacted = RedactValue(nested, null, depth + 1);
                    return _jsonCodec.Serialize(redacted);
                }
                catch
                {
                    return RedactedValue;
                }
            }

            return ContainsCredentialShape(value) ? RedactedValue : value;
        }

        private static bool ContainsCredentialShape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            return BearerValue.IsMatch(value) ||
                   CredentialPrefix.IsMatch(value) ||
                   JwtValue.IsMatch(value);
        }

        private static bool LooksLikeJson(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var trimmed = value.TrimStart();
            return trimmed.StartsWith("{", StringComparison.Ordinal) ||
                   trimmed.StartsWith("[", StringComparison.Ordinal);
        }

        private static bool IsSensitiveProperty(string propertyName)
        {
            if (string.IsNullOrWhiteSpace(propertyName))
                return false;

            var normalized = new StringBuilder(propertyName.Length);
            for (var index = 0; index < propertyName.Length; index++)
            {
                var character = propertyName[index];
                if (char.IsLetterOrDigit(character))
                    normalized.Append(char.ToLowerInvariant(character));
            }

            var value = normalized.ToString();
            return value == "gameaccesstoken" ||
                   value == "clienttoken" ||
                   value == "authorization";
        }
    }
}
