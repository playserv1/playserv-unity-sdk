#if PLAYSERV_HAS_NEWTONSOFT_JSON
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Playserv.Serialization
{
    public sealed partial class NewtonsoftJsonCodec : IPlayServStrictJsonCodec
    {
        public void ValidateResponseType(Type responseType)
        {
            if (responseType == null) throw new ArgumentNullException(nameof(responseType));
            try { CheckContract(responseType, null, null, "$", new HashSet<Type>()); }
            catch (Exception error) { throw PlayServStrictResponseException.Sanitize(error); }
        }

        public T DeserializeStrict<T>(string json)
        {
            ValidateResponseType(typeof(T));
            JToken token;
            try
            {
                if (string.IsNullOrWhiteSpace(json)) throw new JsonReaderException();
                using (var reader = new JsonTextReader(new StringReader(json))
                {
                    DateParseHandling = DateParseHandling.None,
                    MaxDepth = 64
                })
                {
                    token = JToken.ReadFrom(reader, new JsonLoadSettings
                    {
                        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
                    });
                    if (reader.Read()) throw new JsonReaderException();
                }
            }
            catch (Exception)
            {
                throw new PlayServStrictResponseException("$", "valid JSON", "empty or malformed JSON");
            }

            ValidateToken(token, typeof(T), null, null, "$", 0);
            try
            {
                // Deserialize the original text, not the validation tree: otherwise intermediate
                // floating-point tokens would lose precision for decimal DTO members.
                using (var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None })
                    return JsonSerializer.Create(BuildSettings(new JsonCodecOptions { ParseDates = false })).Deserialize<T>(reader);
            }
            catch (Exception)
            {
                // Newtonsoft/converter messages may contain the offending value. Do not retain them.
                throw new PlayServStrictResponseException("$", "deserializable response", "invalid value format");
            }
        }

        private static void CheckConverter(JsonConverter converter, Type type, string path)
        {
            if (converter == null) return;
            if (converter.GetType() == typeof(StringEnumConverter) && type.IsEnum) return;
            throw new PlayServStrictResponseException(path, "supported response contract", "unsupported custom converter");
        }

        private static void CheckContract(Type type, JsonConverter converter, JsonConverter itemConverter,
            string path, HashSet<Type> active)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            var contract = ContractResolver.ResolveContract(type);
            CheckConverter(converter, type, path);
            CheckConverter(contract.Converter, type, path);
            if (!active.Add(type)) return;
            try
            {
                if (IsScalar(type) || type == typeof(object) || type == typeof(JToken) || type == typeof(JObject) || type == typeof(JArray)) return;
                if (contract is JsonArrayContract array)
                {
                    if (array.IsMultidimensionalArray)
                        throw new PlayServStrictResponseException(path, "one-dimensional collection", "unsupported array rank");
                    CheckContract(array.CollectionItemType ?? typeof(object), itemConverter ?? array.ItemConverter,
                        null, path + "[*]", active);
                    return;
                }
                if (contract is JsonDictionaryContract dictionary)
                {
                    if (dictionary.DictionaryKeyType != typeof(string))
                        throw new PlayServStrictResponseException(path, "string dictionary keys", "unsupported key type");
                    CheckContract(dictionary.DictionaryValueType ?? typeof(object), itemConverter ?? dictionary.ItemConverter,
                        null, path + "[*]", active);
                    return;
                }
                if (contract is JsonObjectContract obj)
                {
                    if (type.IsAbstract || type.IsInterface)
                        throw new PlayServStrictResponseException(path, "constructible DTO", "unsupported response type");
                    foreach (var property in obj.Properties)
                        CheckProperty(property, itemConverter ?? obj.ItemConverter, path, active);
                    foreach (var parameter in obj.CreatorParameters)
                        CheckProperty(parameter, itemConverter ?? obj.ItemConverter, path, active);
                    if (obj.ExtensionDataValueType != null)
                        CheckContract(obj.ExtensionDataValueType, null, null, path + "[*]", active);
                    return;
                }
                throw new PlayServStrictResponseException(path, "supported response contract", "unsupported response type");
            }
            finally { active.Remove(type); }
        }

        private static void CheckProperty(JsonProperty property, JsonConverter containerConverter,
            string path, HashSet<Type> active)
        {
            if (!property.Ignored)
                CheckContract(property.PropertyType, property.Converter ?? containerConverter, property.ItemConverter,
                    path + "." + property.PropertyName, active);
        }

        private static bool IsScalar(Type type) => type.IsEnum || (type.IsPrimitive && type != typeof(IntPtr) && type != typeof(UIntPtr) && type != typeof(void)) || type == typeof(decimal) ||
            type == typeof(string) || type == typeof(Guid) || type == typeof(DateTime) ||
            type == typeof(DateTimeOffset) || type == typeof(TimeSpan) || type == typeof(Uri) || type == typeof(byte[]);

        private static void Require(bool condition, string path, string expected, JToken token)
        {
            if (!condition) throw new PlayServStrictResponseException(path, expected, token.Type.ToString());
        }

        private static void ValidateToken(JToken token, Type declaredType, JsonConverter converter,
            JsonConverter itemConverter, string path, int depth)
        {
            if (depth > 64) throw new PlayServStrictResponseException(path, "depth <= 64", "excessive nesting");
            var type = Nullable.GetUnderlyingType(declaredType) ?? declaredType;
            if (token.Type == JTokenType.Null)
            {
                Require(!declaredType.IsValueType || Nullable.GetUnderlyingType(declaredType) != null,
                    path, "non-null value", token);
                return;
            }
            var contract = ContractResolver.ResolveContract(type);
            converter = converter ?? contract.Converter;
            if (type == typeof(object) || type == typeof(JToken)) return;
            if (type == typeof(JObject)) { Require(token.Type == JTokenType.Object, path, "object", token); return; }
            if (type == typeof(JArray)) { Require(token.Type == JTokenType.Array, path, "array", token); return; }
            if (typeof(JToken).IsAssignableFrom(type))
                throw new PlayServStrictResponseException(path, "supported JSON token type", "unsupported response type");
            if (type.IsEnum)
            {
                if (converter is StringEnumConverter enumConverter && token.Type == JTokenType.String)
                {
                    try { token.ToObject(type, new JsonSerializer { Converters = { enumConverter } }); }
                    catch (Exception) { throw new PlayServStrictResponseException(path, "declared string enum", "invalid string format"); }
                }
                else
                {
                    if (converter is StringEnumConverter numericEnum && !numericEnum.AllowIntegerValues)
                        Require(false, path, "string enum", token);
                    ValidateNumber(token, Enum.GetUnderlyingType(type), path);
                }
                return;
            }
            if (type == typeof(bool)) { Require(token.Type == JTokenType.Boolean, path, "boolean", token); return; }
            if (type == typeof(string) || type == typeof(char) || type == typeof(Guid) || type == typeof(DateTime) ||
                type == typeof(DateTimeOffset) || type == typeof(TimeSpan) || type == typeof(Uri) || type == typeof(byte[]))
            {
                Require(token.Type == JTokenType.String, path, "string", token);
                if (type == typeof(char)) Require(((string)token).Length == 1, path, "single-character string", token);
                // Validate format at the field path, without including the value in diagnostics.
                try { token.ToObject(type, JsonSerializer.Create(BuildSettings(new JsonCodecOptions { ParseDates = false }))); }
                catch (Exception) { throw new PlayServStrictResponseException(path, type.Name + " string", "invalid string format"); }
                return;
            }
            if (type.IsPrimitive || type == typeof(decimal)) { ValidateNumber(token, type, path); return; }
            if (contract is JsonArrayContract array)
            {
                Require(token.Type == JTokenType.Array, path, "array", token);
                var index = 0;
                foreach (var child in token.Children())
                    ValidateToken(child, array.CollectionItemType ?? typeof(object), itemConverter ?? array.ItemConverter,
                        null, path + "[" + index++ + "]", depth + 1);
                return;
            }
            if (contract is JsonDictionaryContract dictionary)
            {
                Require(token.Type == JTokenType.Object, path, "object", token);
                foreach (var property in ((JObject)token).Properties())
                    ValidateToken(property.Value, dictionary.DictionaryValueType ?? typeof(object),
                        itemConverter ?? dictionary.ItemConverter, null, path + "[*]", depth + 1);
                return;
            }
            if (contract is JsonObjectContract obj)
            {
                Require(token.Type == JTokenType.Object, path, "object", token);
                ValidateProperties((JObject)token, obj.Properties, obj, itemConverter, path, depth);
                ValidateProperties((JObject)token, obj.CreatorParameters, obj, itemConverter, path, depth);
                if (obj.ExtensionDataValueType != null)
                    foreach (var property in ((JObject)token).Properties())
                        if (obj.Properties.GetClosestMatchProperty(property.Name) == null)
                            ValidateToken(property.Value, obj.ExtensionDataValueType, null, null, path + "[*]", depth + 1);
                return;
            }
            throw new PlayServStrictResponseException(path, "supported response contract", "unsupported response type");
        }

        private static void ValidateProperties(JObject token, JsonPropertyCollection properties, JsonObjectContract contract,
            JsonConverter itemConverter, string path, int depth)
        {
            foreach (var property in properties)
            {
                if (property.Ignored) continue;
                var required = property.Required != Required.Default ? property.Required : contract.ItemRequired ?? Required.Default;
                if (!token.TryGetValue(property.PropertyName, StringComparison.OrdinalIgnoreCase, out var value))
                {
                    if (required == Required.Always || required == Required.AllowNull)
                        throw new PlayServStrictResponseException(path + "." + property.PropertyName, "required field", "missing");
                    continue;
                }
                var fieldPath = path + "." + property.PropertyName;
                if (required == Required.Always || required == Required.DisallowNull)
                    Require(value.Type != JTokenType.Null, fieldPath, "non-null field", value);
                ValidateToken(value, property.PropertyType, property.Converter ?? itemConverter ?? contract.ItemConverter,
                    property.ItemConverter, fieldPath, depth + 1);
            }
            // Newtonsoft also accepts case-insensitive aliases. Validate every occurrence, not just
            // the exact-name winner, so duplicate casing cannot hide a coerced value.
            foreach (var input in token.Properties())
            {
                var property = properties.GetClosestMatchProperty(input.Name);
                if (property == null || property.Ignored || input.Name == property.PropertyName) continue;
                ValidateToken(input.Value, property.PropertyType, property.Converter ?? itemConverter ?? contract.ItemConverter,
                    property.ItemConverter, path + "." + property.PropertyName, depth + 1);
            }
        }

        private static void ValidateNumber(JToken token, Type type, string path)
        {
            Require(token.Type == JTokenType.Integer || token.Type == JTokenType.Float, path, "number", token);
            var text = token.ToString(Formatting.None);
            if (type == typeof(double) || type == typeof(float))
            {
                var valid = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
                    !double.IsInfinity(value) && !double.IsNaN(value);
                if (type == typeof(float)) valid &= value >= -float.MaxValue && value <= float.MaxValue;
                Require(valid, path, "finite " + type.Name + " number", token);
                return;
            }
            Require(decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number),
                path, "in-range " + type.Name + " number", token);
            if (type == typeof(decimal)) return;
            // Integer contracts require an integer JSON token; do not round a parsed floating-point
            // value (in particular a fraction near Int64/UInt64 bounds) into an accepted integer.
            Require(token.Type == JTokenType.Integer, path, type.Name + " integer", token);
            decimal min, max;
            switch (Type.GetTypeCode(type))
            {
                case TypeCode.SByte: min = sbyte.MinValue; max = sbyte.MaxValue; break;
                case TypeCode.Byte: min = byte.MinValue; max = byte.MaxValue; break;
                case TypeCode.Int16: min = short.MinValue; max = short.MaxValue; break;
                case TypeCode.UInt16: min = ushort.MinValue; max = ushort.MaxValue; break;
                case TypeCode.Int32: min = int.MinValue; max = int.MaxValue; break;
                case TypeCode.UInt32: min = uint.MinValue; max = uint.MaxValue; break;
                case TypeCode.Int64: min = long.MinValue; max = long.MaxValue; break;
                case TypeCode.UInt64: min = ulong.MinValue; max = ulong.MaxValue; break;
                default: throw new PlayServStrictResponseException(path, "supported numeric type", "unsupported response type");
            }
            Require(number == decimal.Truncate(number) && number >= min && number <= max,
                path, "in-range " + type.Name + " integer", token);
        }
    }
}
#endif
