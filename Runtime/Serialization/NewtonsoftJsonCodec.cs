using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
#if PLAYSERV_HAS_NEWTONSOFT_JSON
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
#endif

namespace Playserv.Serialization
{
#if PLAYSERV_HAS_NEWTONSOFT_JSON
    public sealed partial class NewtonsoftJsonCodec : IJsonCodec
    {
        private static readonly IContractResolver ContractResolver = new PlayServJsonNameContractResolver();

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

            if (value is IDictionary<string, object> || value is IList<object>)
                return ToPlainValue(value, options);

            if (value is JToken token)
                return ConvertTokenToPlain(token);

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

        public object ToPlainValue(object value, JsonCodecOptions options = null)
        {
            if (value == null)
                return null;

            if (value is string text)
            {
                try
                {
                    return ParseToPlainValue(JsonConvert.SerializeObject(text), options);
                }
                catch (JsonCodecException)
                {
                    return text;
                }
            }

            if (value is JToken token)
                return ConvertTokenToPlain(token);

            if (value is IDictionary<string, object> dictionary)
                return CloneDictionary(dictionary);

            if (value is IList<object> list)
                return CloneList(list);

            if (value is IDictionary nonGenericDictionary)
                return CloneDictionary(ToStringObjectDictionary(nonGenericDictionary));

            if (value is IList nonGenericList && value is not byte[])
                return CloneList(ToObjectList(nonGenericList));

            try
            {
                var json = Serialize(value, options);
                return ParseToPlainValue(json, options);
            }
            catch (JsonCodecException)
            {
                return value;
            }
        }

        public object ParseToPlainValue(string json, JsonCodecOptions options = null)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                return ConvertTokenToPlain(JToken.Parse(json));
            }
            catch (JsonException ex)
            {
                throw new JsonCodecException("Failed to parse JSON document.", ex);
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
                .FirstOrDefault(x => x.CanRead && MatchesPropertyName(x, propertyName, comparison));

            if (property != null)
            {
                propertyValue = property.GetValue(value, null);
                return true;
            }

            var field = value.GetType()
                .GetFields(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(x => MatchesFieldName(x, propertyName, comparison));

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

            var settings = new JsonSerializerSettings
            {
                DateParseHandling = options.ParseDates ? DateParseHandling.DateTime : DateParseHandling.None,
                NullValueHandling = options.IncludeNullValues ? NullValueHandling.Include : NullValueHandling.Ignore,
                MissingMemberHandling = options.IgnoreMissingMembers ? MissingMemberHandling.Ignore : MissingMemberHandling.Error,
                MetadataPropertyHandling = options.IgnoreMetadataProperties
                    ? MetadataPropertyHandling.Ignore
                    : MetadataPropertyHandling.Default,
                ContractResolver = ContractResolver
            };

            if (options.CustomConverters != null)
            {
                foreach (var converter in options.CustomConverters)
                {
                    if (converter is JsonConverter jsonConverter)
                        settings.Converters.Add(jsonConverter);
                }
            }

            return settings;
        }

        private static object ConvertTokenToPlain(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined)
                return null;

            if (token is JObject obj)
            {
                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var property in obj.Properties())
                {
                    result[property.Name] = ConvertTokenToPlain(property.Value);
                }

                return result;
            }

            if (token is JArray array)
            {
                var result = new List<object>(array.Count);
                foreach (var item in array)
                {
                    result.Add(ConvertTokenToPlain(item));
                }

                return result;
            }

            if (token is JValue value)
                return value.Value;

            return token.ToString(Formatting.None);
        }

        private static Dictionary<string, object> CloneDictionary(IDictionary<string, object> dictionary)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            if (dictionary == null)
                return result;

            foreach (var pair in dictionary)
            {
                result[pair.Key] = ClonePlainValue(pair.Value);
            }

            return result;
        }

        private static List<object> CloneList(IEnumerable<object> values)
        {
            var result = new List<object>();
            if (values == null)
                return result;

            foreach (var value in values)
            {
                result.Add(ClonePlainValue(value));
            }

            return result;
        }

        private static object ClonePlainValue(object value)
        {
            if (value == null)
                return null;

            if (value is IDictionary<string, object> dictionary)
                return CloneDictionary(dictionary);

            if (value is IList<object> list)
                return CloneList(list);

            if (value is IDictionary nonGenericDictionary)
                return CloneDictionary(ToStringObjectDictionary(nonGenericDictionary));

            if (value is IList nonGenericList && value is not byte[])
                return CloneList(ToObjectList(nonGenericList));

            if (value is JToken token)
                return ConvertTokenToPlain(token);

            return value;
        }

        private static Dictionary<string, object> ToStringObjectDictionary(IDictionary dictionary)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            if (dictionary == null)
                return result;

            foreach (DictionaryEntry entry in dictionary)
            {
                var key = entry.Key?.ToString();
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                result[key] = entry.Value;
            }

            return result;
        }

        private static List<object> ToObjectList(IList list)
        {
            var result = new List<object>();
            if (list == null)
                return result;

            foreach (var item in list)
            {
                result.Add(item);
            }

            return result;
        }

        private static bool MatchesPropertyName(PropertyInfo property, string name, StringComparison comparison)
        {
            if (property == null)
                return false;

            if (string.Equals(property.Name, name, comparison))
                return true;

            var attributeName = GetCustomJsonName(property);
            return !string.IsNullOrWhiteSpace(attributeName) &&
                   string.Equals(attributeName, name, comparison);
        }

        private static bool MatchesFieldName(FieldInfo field, string name, StringComparison comparison)
        {
            if (field == null)
                return false;

            if (string.Equals(field.Name, name, comparison))
                return true;

            var attributeName = GetCustomJsonName(field);
            return !string.IsNullOrWhiteSpace(attributeName) &&
                   string.Equals(attributeName, name, comparison);
        }

        private static string GetCustomJsonName(MemberInfo member)
        {
            var attribute = member.GetCustomAttribute<PlayServJsonNameAttribute>(true);
            if (!string.IsNullOrWhiteSpace(attribute?.Name))
                return attribute.Name;

            var schemaField = member.GetCustomAttributes(true).FirstOrDefault(candidate =>
                string.Equals(
                    candidate.GetType().FullName,
                    "Playserv.Schema.PlayServFieldAttribute",
                    StringComparison.Ordinal));
            if (schemaField == null)
                return null;

            var nameProperty = schemaField.GetType().GetProperty("Name", BindingFlags.Public | BindingFlags.Instance);
            return nameProperty?.GetValue(schemaField, null) as string;
        }

        private static bool HasPlayServIgnore(MemberInfo member)
        {
            return member.GetCustomAttributes(true).Any(candidate =>
                string.Equals(
                    candidate.GetType().FullName,
                    "Playserv.Schema.PlayServIgnoreAttribute",
                    StringComparison.Ordinal));
        }

        private sealed class PlayServJsonNameContractResolver : DefaultContractResolver
        {
            protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
            {
                var property = base.CreateProperty(member, memberSerialization);
                var attributeName = GetCustomJsonName(member);

                if (!string.IsNullOrWhiteSpace(attributeName))
                    property.PropertyName = attributeName;
                if (HasPlayServIgnore(member))
                    property.Ignored = true;

                return property;
            }
        }
    }
#else
    public sealed class NewtonsoftJsonCodec : IJsonCodec
    {
        public string Serialize(object value, JsonCodecOptions options = null)
        {
            throw NewtonsoftJsonDependency.CreateMissingException();
        }

        public T Deserialize<T>(string json, JsonCodecOptions options = null)
        {
            throw NewtonsoftJsonDependency.CreateMissingException();
        }

        public object Deserialize(string json, Type type, JsonCodecOptions options = null)
        {
            throw NewtonsoftJsonDependency.CreateMissingException();
        }

        public T Convert<T>(object value, JsonCodecOptions options = null)
        {
            throw NewtonsoftJsonDependency.CreateMissingException();
        }

        public object Convert(object value, Type type, JsonCodecOptions options = null)
        {
            throw NewtonsoftJsonDependency.CreateMissingException();
        }

        public string ToCanonicalJson(object value, JsonCodecOptions options = null)
        {
            throw NewtonsoftJsonDependency.CreateMissingException();
        }

        public object Clone(object value, JsonCodecOptions options = null)
        {
            throw NewtonsoftJsonDependency.CreateMissingException();
        }

        public object ToPlainValue(object value, JsonCodecOptions options = null)
        {
            throw NewtonsoftJsonDependency.CreateMissingException();
        }

        public object ParseToPlainValue(string json, JsonCodecOptions options = null)
        {
            throw NewtonsoftJsonDependency.CreateMissingException();
        }

        public bool TryGetProperty(object value, string propertyName, bool ignoreCase, out object propertyValue)
        {
            propertyValue = null;
            return false;
        }

        public bool TryGetFirstPropertyValue(object value, out object propertyValue)
        {
            propertyValue = null;
            return false;
        }
    }
#endif
}
