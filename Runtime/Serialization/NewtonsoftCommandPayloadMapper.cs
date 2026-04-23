using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Playserv.Proxy.Common;

namespace Playserv.Serialization
{
    public sealed class NewtonsoftCommandPayloadMapper : ICommandPayloadMapper
    {
        public string BuildKeepAlivePayloadJson(string eventType, string payloadJson)
        {
            var payloadToken = ParsePayloadToken(payloadJson) ?? new JObject();
            var effectiveEventType = string.IsNullOrWhiteSpace(eventType) ? "KeepAlive" : eventType;

            return new JObject
            {
                ["EventType"] = effectiveEventType,
                ["Event"] = "KeepAlive",
                ["Payload"] = payloadToken
            }.ToString(Formatting.None);
        }

        public string NormalizePayloadForType(string payloadJson, Type type)
        {
            if (string.IsNullOrWhiteSpace(payloadJson))
                return payloadJson;

            JObject payloadObject;
            try
            {
                payloadObject = JObject.Parse(payloadJson);
            }
            catch (JsonException)
            {
                return payloadJson;
            }

            if (!payloadObject.ContainsKey("EventType") &&
                payloadObject.TryGetValue("Event", StringComparison.OrdinalIgnoreCase, out var eventToken) &&
                HasStringMember(type, "EventType"))
            {
                payloadObject["EventType"] = eventToken.Type == JTokenType.String
                    ? eventToken
                    : eventToken.ToString(Formatting.None);
            }

            if (payloadObject.TryGetValue("Payload", StringComparison.OrdinalIgnoreCase, out var commandPayload) &&
                commandPayload.Type != JTokenType.String &&
                commandPayload.Type != JTokenType.Null &&
                HasStringMember(type, "Payload"))
            {
                payloadObject["Payload"] = commandPayload.ToString(Formatting.None);
            }

            if (payloadObject.TryGetValue("error", StringComparison.OrdinalIgnoreCase, out var errorPayload) &&
                errorPayload.Type != JTokenType.String &&
                errorPayload.Type != JTokenType.Null &&
                HasStringMember(type, "error"))
            {
                payloadObject["error"] = errorPayload.Type == JTokenType.Object &&
                                         errorPayload["message"]?.Type == JTokenType.String
                    ? errorPayload["message"].ToString()
                    : errorPayload.ToString(Formatting.None);
            }

            if (HasStringMember(type, "error") &&
                (!payloadObject.TryGetValue("error", StringComparison.OrdinalIgnoreCase, out var existingError) ||
                 existingError.Type == JTokenType.Null ||
                 (existingError.Type == JTokenType.String && string.IsNullOrWhiteSpace(existingError.ToString()))) &&
                payloadObject.TryGetValue("Code", StringComparison.OrdinalIgnoreCase, out var codePayload) &&
                codePayload.Type != JTokenType.Null)
            {
                payloadObject["error"] = codePayload.Type == JTokenType.String
                    ? codePayload.ToString()
                    : codePayload.ToString(Formatting.None);
            }

            if (HasStringMember(type, "timestamp") &&
                (!payloadObject.TryGetValue("timestamp", StringComparison.OrdinalIgnoreCase, out var existingTimestamp) ||
                 existingTimestamp.Type == JTokenType.Null ||
                 (existingTimestamp.Type == JTokenType.String && string.IsNullOrWhiteSpace(existingTimestamp.ToString()))) &&
                payloadObject.TryGetValue("TimestampUtc", StringComparison.OrdinalIgnoreCase, out var timestampPayload) &&
                timestampPayload.Type != JTokenType.Null)
            {
                payloadObject["timestamp"] = timestampPayload.Type == JTokenType.String
                    ? timestampPayload.ToString()
                    : timestampPayload.ToString(Formatting.None);
            }

            return payloadObject.ToString(Formatting.None);
        }

        public bool TryDeserializeAsCommandError(string payloadJson, out CommandErrorResponse response)
        {
            response = null;

            if (string.IsNullOrWhiteSpace(payloadJson))
                return false;

            JObject payloadObject;
            try
            {
                payloadObject = JObject.Parse(payloadJson);
            }
            catch (JsonException)
            {
                return false;
            }

            if (!payloadObject.TryGetValue("error", StringComparison.OrdinalIgnoreCase, out var errorToken) &&
                !payloadObject.TryGetValue("message", StringComparison.OrdinalIgnoreCase, out _))
            {
                return false;
            }

            var error = string.Empty;
            if (errorToken != null && errorToken.Type != JTokenType.Null)
            {
                if (errorToken.Type == JTokenType.String)
                {
                    error = errorToken.ToString();
                }
                else if (errorToken.Type == JTokenType.Object &&
                         errorToken["message"]?.Type == JTokenType.String)
                {
                    error = errorToken["message"].ToString();
                }
                else
                {
                    error = errorToken.ToString(Formatting.None);
                }
            }

            response = new CommandErrorResponse
            {
                error = error,
                message = payloadObject["message"]?.ToString() ?? string.Empty,
                timestamp = payloadObject["timestamp"]?.ToString() ?? string.Empty
            };
            return true;
        }

        private static bool HasStringMember(Type type, string memberName)
        {
            var field = type.GetField(memberName);
            if (field != null && field.FieldType == typeof(string))
                return true;

            var property = type.GetProperty(memberName);
            return property != null && property.PropertyType == typeof(string);
        }

        private static JToken ParsePayloadToken(string payloadJson)
        {
            if (string.IsNullOrWhiteSpace(payloadJson))
                return new JObject();

            try
            {
                return JToken.Parse(payloadJson);
            }
            catch (JsonException)
            {
                return new JObject();
            }
        }
    }
}
