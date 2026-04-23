using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Playserv.Serialization
{
    public sealed class NewtonsoftJsonCodec : IJsonCodec
    {
        public string Serialize(object value, JsonCodecOptions options = null)
        {
            try
            {
                return JsonConvert.SerializeObject(value, BuildSettings(options));
            }
            catch (JsonException ex)
            {
                throw new JsonCodecException("Failed to serialize JSON payload.", ex);
            }
        }

        public T Deserialize<T>(string json, JsonCodecOptions options = null)
        {
            try
            {
                return JsonConvert.DeserializeObject<T>(json, BuildSettings(options));
            }
            catch (JsonException ex)
            {
                throw new JsonCodecException($"Failed to deserialize JSON payload to {typeof(T).Name}.", ex);
            }
        }

        public object Deserialize(string json, Type type, JsonCodecOptions options = null)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            try
            {
                return JsonConvert.DeserializeObject(json, type, BuildSettings(options));
            }
            catch (JsonException ex)
            {
                throw new JsonCodecException($"Failed to deserialize JSON payload to {type.Name}.", ex);
            }
        }

        public T Convert<T>(object value, JsonCodecOptions options = null)
        {
            if (value is T typed)
                return typed;

            var json = value as string ?? Serialize(value, options);
            return Deserialize<T>(json, options);
        }

        public object Convert(object value, Type type, JsonCodecOptions options = null)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            if (value != null && type.IsInstanceOfType(value))
                return value;

            var json = value as string ?? Serialize(value, options);
            return Deserialize(json, type, options);
        }

        public string ToCanonicalJson(object value, JsonCodecOptions options = null)
        {
            if (value == null)
                return string.Empty;

            if (value is string text)
                return text;

            if (value is JToken token)
                return token.ToString(Formatting.None);

            return JsonConvert.SerializeObject(value, BuildSettings(options));
        }

        public object Clone(object value, JsonCodecOptions options = null)
        {
            if (value == null)
                return null;

            if (value is string text)
                return text;

            if (value is JToken token)
                return token.DeepClone();

            var json = Serialize(value, options);
            try
            {
                return JToken.Parse(json);
            }
            catch (JsonException)
            {
                return json;
            }
        }

        public bool TryGetProperty(object value, string propertyName, bool ignoreCase, out object propertyValue)
        {
            propertyValue = null;

            if (string.IsNullOrWhiteSpace(propertyName) || value == null)
                return false;

            if (value is JObject obj)
                return TryGetProperty(obj, propertyName, ignoreCase, out propertyValue);

            if (value is IDictionary<string, object> dict)
                return TryGetProperty(dict, propertyName, ignoreCase, out propertyValue);

            if (value is JToken token && token.Type == JTokenType.Object)
                return TryGetProperty((JObject)token, propertyName, ignoreCase, out propertyValue);

            var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var property = value.GetType()
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(x => x.CanRead && string.Equals(x.Name, propertyName, comparison));

            if (property != null)
            {
                propertyValue = property.GetValue(value, null);
                return true;
            }

            var field = value.GetType()
                .GetFields(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(x => string.Equals(x.Name, propertyName, comparison));

            if (field == null)
                return false;

            propertyValue = field.GetValue(value);
            return true;
        }

        public bool TryGetFirstPropertyValue(object value, out object propertyValue)
        {
            propertyValue = null;
            if (value == null)
                return false;

            if (value is JObject obj)
            {
                var first = obj.Properties().FirstOrDefault();
                if (first == null)
                    return false;

                propertyValue = first.Value;
                return true;
            }

            if (value is IDictionary<string, object> dict)
            {
                foreach (var pair in dict)
                {
                    propertyValue = pair.Value;
                    return true;
                }

                return false;
            }

            if (value is JToken token && token.Type == JTokenType.Object)
                return TryGetFirstPropertyValue((JObject)token, out propertyValue);

            var property = value.GetType()
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(x => x.CanRead && x.GetIndexParameters().Length == 0);

            if (property == null)
                return false;

            propertyValue = property.GetValue(value, null);
            return true;
        }

        private static bool TryGetProperty(JObject obj, string propertyName, bool ignoreCase, out object propertyValue)
        {
            propertyValue = null;
            if (obj == null)
                return false;

            if (obj.TryGetValue(propertyName, StringComparison.Ordinal, out var exact))
            {
                propertyValue = exact;
                return true;
            }

            if (!ignoreCase)
                return false;

            if (!obj.TryGetValue(propertyName, StringComparison.OrdinalIgnoreCase, out var insensitive))
                return false;

            propertyValue = insensitive;
            return true;
        }

        private static bool TryGetProperty(IDictionary<string, object> dict, string propertyName, bool ignoreCase, out object propertyValue)
        {
            propertyValue = null;
            if (dict == null)
                return false;

            if (dict.TryGetValue(propertyName, out var exact))
            {
                propertyValue = exact;
                return true;
            }

            if (!ignoreCase)
                return false;

            foreach (var pair in dict)
            {
                if (!string.Equals(pair.Key, propertyName, StringComparison.OrdinalIgnoreCase))
                    continue;

                propertyValue = pair.Value;
                return true;
            }

            return false;
        }

        private static JsonSerializerSettings BuildSettings(JsonCodecOptions options)
        {
            if (options == null)
                options = new JsonCodecOptions();

            return new JsonSerializerSettings
            {
                DateParseHandling = options.ParseDates ? DateParseHandling.DateTime : DateParseHandling.None,
                NullValueHandling = options.IncludeNullValues ? NullValueHandling.Include : NullValueHandling.Ignore,
                MissingMemberHandling = options.IgnoreMissingMembers ? MissingMemberHandling.Ignore : MissingMemberHandling.Error
            };
        }
    }
}
