using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Playserv.Events.Requests;
using Playserv.Proxy.Interfaces;

namespace Playserv.Proxy.Implementation
{
    public sealed class JsonSerializer : IMessageSerializer
    {
        public MessageEnvelope Serialize<T>(T command, string moduleName = null)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            if (command is EventMessage eventMessage &&
                string.Equals(eventMessage.EventType, "KeepAlive", StringComparison.OrdinalIgnoreCase))
            {
                // KeepAlive must satisfy both server handlers:
                // - transport keepalive handler expects "Event"
                // - events module expects "EventType"
                var keepAlivePayload = BuildKeepAlivePayload(eventMessage);
                return new MessageEnvelope("EventMessage", keepAlivePayload.ToString(Formatting.None));
            }

            var commandType = command.GetType();
            var typeName = commandType.Name;

            if (moduleName != null)
            {
                typeName = $"{moduleName}.{typeName}";
            }
            else
            {
                var commandNamespace = commandType.Namespace;
                
                if (!string.IsNullOrEmpty(commandNamespace) &&
                    commandNamespace.StartsWith("Playserv.Events", StringComparison.Ordinal))
                {
                    typeName = $"module_events.{typeName}";
                }
            }

            var payloadJson = JsonConvert.SerializeObject(command);
            payloadJson = NormalizeOutgoingPayloadForType(payloadJson, commandType);

            return new MessageEnvelope(typeName, payloadJson);
        }

        public object Deserialize(MessageEnvelope envelope)
        {
            if (string.IsNullOrWhiteSpace(envelope.Command))
                throw new InvalidOperationException("MessageEnvelope missing command type.");

            if (string.IsNullOrWhiteSpace(envelope.Payload))
                throw new InvalidOperationException("MessageEnvelope missing payload.");

            var commandName = envelope.Command;
            var dotIndex = commandName.IndexOf('.');
            if (dotIndex >= 0 && dotIndex < commandName.Length - 1)
            {
                commandName = commandName.Substring(dotIndex + 1);
            }

            var type = FindType(commandName);
            
            if (type == null)
                throw new InvalidOperationException($"Unknown command type: {envelope.Command}");

            var normalizedPayload = NormalizePayloadForType(envelope.Payload, type);
            var cmd = JsonConvert.DeserializeObject(normalizedPayload, type);
            
            if (cmd == null)
                throw new InvalidOperationException("Failed to deserialize payload.");

            return cmd;
        }

        private Type FindType(string typeName)
        {
            var type = Type.GetType(typeName);
            if (type != null)
                return type;

            foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(typeName);
                if (type != null)
                    return type;

                var types = assembly.GetTypes();
                foreach (var t in types)
                {
                    if (t.Name == typeName)
                    {
                        return t;
                    }
                }
            }

            var pascalCaseName = ToPascalCase(typeName);
            var responseName = pascalCaseName + "Response";
            
            foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                var types = assembly.GetTypes();
                foreach (var t in types)
                {
                    if (t.Name == responseName || t.Name == pascalCaseName)
                    {
                        return t;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Normalizes incoming payload for backward/forward compatible deserialization.
        /// </summary>
        /// <remarks>
        /// Handles two known transport variants:
        /// 1) "Event" field instead of "EventType"
        /// 2) Object/array payload token for commands where "Payload" member is string
        /// </remarks>
        private static string NormalizePayloadForType(string payloadJson, Type type)
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
                // Not an object payload (or not JSON) - use as-is.
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

            return payloadObject.ToString(Formatting.None);
        }

        private static bool HasStringMember(Type type, string memberName)
        {
            var field = type.GetField(memberName);
            if (field != null && field.FieldType == typeof(string))
                return true;

            var property = type.GetProperty(memberName);
            return property != null && property.PropertyType == typeof(string);
        }

        /// <summary>
        /// Normalizes outgoing event envelopes to the transport wire format expected by server-side proxies:
        /// - string payload containing JSON -> object/array payload token
        /// </summary>
        private static string NormalizeOutgoingPayloadForType(string payloadJson, Type type)
        {
            if (string.IsNullOrWhiteSpace(payloadJson))
                return payloadJson;

            var commandNamespace = type.Namespace;
            if (string.IsNullOrEmpty(commandNamespace) ||
                !commandNamespace.StartsWith("Playserv.Events", StringComparison.Ordinal))
            {
                return payloadJson;
            }

            JObject payloadObject;
            try
            {
                payloadObject = JObject.Parse(payloadJson);
            }
            catch (JsonException)
            {
                return payloadJson;
            }

            if (payloadObject.TryGetValue("Payload", StringComparison.OrdinalIgnoreCase, out var commandPayloadToken) &&
                commandPayloadToken.Type == JTokenType.String)
            {
                var rawPayload = commandPayloadToken.Value<string>();
                if (!string.IsNullOrWhiteSpace(rawPayload))
                {
                    var trimmedPayload = rawPayload.Trim();
                    if ((trimmedPayload.StartsWith("{", StringComparison.Ordinal) && trimmedPayload.EndsWith("}", StringComparison.Ordinal)) ||
                        (trimmedPayload.StartsWith("[", StringComparison.Ordinal) && trimmedPayload.EndsWith("]", StringComparison.Ordinal)))
                    {
                        try
                        {
                            payloadObject["Payload"] = JToken.Parse(trimmedPayload);
                        }
                        catch (JsonException)
                        {
                            // Keep original string payload if it is not valid JSON.
                        }
                    }
                }
            }

            return payloadObject.ToString(Formatting.None);
        }

        private static JObject BuildKeepAlivePayload(EventMessage eventMessage)
        {
            var payloadToken = ParsePayloadToken(eventMessage.Payload) ?? new JObject();
            var eventType = string.IsNullOrWhiteSpace(eventMessage.EventType)
                ? "KeepAlive"
                : eventMessage.EventType;

            return new JObject
            {
                ["EventType"] = eventType,
                ["Event"] = "KeepAlive",
                ["Payload"] = payloadToken
            };
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

        private string ToPascalCase(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            var parts = input.Split(new[] { '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
            var result = "";
            foreach (var part in parts)
            {
                if (part.Length > 0)
                {
                    result += char.ToUpperInvariant(part[0]) + part.Substring(1).ToLowerInvariant();
                }
            }
            return result;
        }
    }
}
