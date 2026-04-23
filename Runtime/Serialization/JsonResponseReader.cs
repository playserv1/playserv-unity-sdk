using System;
using System.Collections.Generic;
using System.Linq;

namespace Playserv.Serialization
{
    public static class JsonResponseReader
    {
        private static readonly IJsonCodec DefaultJsonCodec = new NewtonsoftJsonCodec();

        public static object ParseDocument(string json, IJsonCodec jsonCodec = null)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                return (jsonCodec ?? DefaultJsonCodec).ParseToPlainValue(json);
            }
            catch (JsonCodecException ex)
            {
                throw new InvalidOperationException("Invalid JSON document.", ex);
            }
        }

        public static string GetStringValueIgnoreCase(string json, string key, IJsonCodec jsonCodec = null)
        {
            var document = ParseDocument(json, jsonCodec);
            return GetStringValueIgnoreCase(document, key, jsonCodec);
        }

        public static string GetStringValueIgnoreCase(object document, string key, IJsonCodec jsonCodec = null)
        {
            if (document == null || string.IsNullOrWhiteSpace(key))
                return string.Empty;

            var codec = jsonCodec ?? DefaultJsonCodec;
            if (!codec.TryGetProperty(document, key, ignoreCase: true, out var value) || value == null)
                return string.Empty;

            return value is string text
                ? text.Trim()
                : (codec.ToCanonicalJson(value)?.Trim() ?? string.Empty);
        }

        public static string[] GetStringArrayValueIgnoreCase(string json, string key, IJsonCodec jsonCodec = null)
        {
            var document = ParseDocument(json, jsonCodec);
            return GetStringArrayValueIgnoreCase(document, key, jsonCodec);
        }

        public static string[] GetStringArrayValueIgnoreCase(object document, string key, IJsonCodec jsonCodec = null)
        {
            if (document == null || string.IsNullOrWhiteSpace(key))
                return Array.Empty<string>();

            var codec = jsonCodec ?? DefaultJsonCodec;
            if (!codec.TryGetProperty(document, key, ignoreCase: true, out var value) || value == null)
                return Array.Empty<string>();

            if (value is IList<object> list)
            {
                return list
                    .Select(item => item == null
                        ? string.Empty
                        : item is string text
                            ? text.Trim()
                            : (codec.ToCanonicalJson(item)?.Trim() ?? string.Empty))
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .ToArray();
            }

            var single = value is string singleText
                ? singleText.Trim()
                : (codec.ToCanonicalJson(value)?.Trim() ?? string.Empty);

            return string.IsNullOrWhiteSpace(single)
                ? Array.Empty<string>()
                : new[] { single };
        }
    }
}
