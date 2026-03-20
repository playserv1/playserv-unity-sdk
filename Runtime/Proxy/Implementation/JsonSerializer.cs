using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Playserv.DataSubscription.Responses;
using Playserv.Events.Requests;
using Playserv.Events.Responses;
using Playserv.RPC;
using Playserv.Proxy.Common;
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

            return new MessageEnvelope(typeName, payloadJson);
        }

        public object Deserialize(MessageEnvelope envelope)
        {
            if (string.IsNullOrWhiteSpace(envelope.Command))
                throw new InvalidOperationException("MessageEnvelope missing command type.");

            if (string.IsNullOrWhiteSpace(envelope.Payload))
                throw new InvalidOperationException("MessageEnvelope missing payload.");

            var commandName = envelope.Command;
            var dotIndex = commandName.LastIndexOf('.');
            if (dotIndex >= 0 && dotIndex < commandName.Length - 1)
            {
                commandName = commandName.Substring(dotIndex + 1);
            }
            commandName = commandName.Trim();

            var type = FindType(commandName);
            
            if (type == null)
                throw new InvalidOperationException($"Unknown command type: {envelope.Command}");

            var normalizedPayload = NormalizePayloadForType(envelope.Payload, type);
            object cmd;
            try
            {
                cmd = JsonConvert.DeserializeObject(normalizedPayload, type);
            }
            catch (JsonException)
            {
                if (TryDeserializeAsCommandError(envelope.Payload, out var fallback))
                    return fallback;

                throw;
            }
            
            if (cmd == null)
                throw new InvalidOperationException("Failed to deserialize payload.");

            return cmd;
        }

        private Type FindType(string typeName)
        {
            if (TryResolveKnownCommandType(typeName, out var knownType))
                return knownType;

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

        private static bool TryResolveKnownCommandType(string commandName, out Type type)
        {
            if (string.IsNullOrWhiteSpace(commandName))
            {
                type = null;
                return false;
            }

            commandName = commandName.Trim();

            if (string.Equals(commandName, "BroadcastEvent", StringComparison.Ordinal))
            {
                // Compatibility path for proxy broadcasts coming from module_rpc host bridge.
                // Payload shape matches EventMessage, but command name can be "BroadcastEvent".
                type = typeof(EventMessage);
                return true;
            }

            if (string.Equals(commandName, "error", StringComparison.OrdinalIgnoreCase))
            {
                type = typeof(CommandErrorResponse);
                return true;
            }

            if (string.Equals(commandName, "Error", StringComparison.Ordinal) ||
                string.Equals(commandName, "CommandErrorResponse", StringComparison.Ordinal) ||
                string.Equals(commandName, "RpcErrorResponse", StringComparison.Ordinal) ||
                commandName.EndsWith("+Error", StringComparison.OrdinalIgnoreCase))
            {
                type = typeof(CommandErrorResponse);
                return true;
            }

            if (string.Equals(commandName, "ErrorResponse", StringComparison.Ordinal))
            {
                type = typeof(ErrorResponse);
                return true;
            }

            if (string.Equals(commandName, "ParseErrorResponse", StringComparison.Ordinal))
            {
                type = typeof(ParseErrorResponse);
                return true;
            }

            if (string.Equals(commandName, "ValidationErrorResponse", StringComparison.Ordinal))
            {
                type = typeof(ValidationErrorResponse);
                return true;
            }

            if (string.Equals(commandName, "InvokeRpcResponse", StringComparison.Ordinal))
            {
                type = typeof(InvokeRpcResponse);
                return true;
            }

            if (string.Equals(commandName, "DataGetResponse", StringComparison.Ordinal))
            {
                type = typeof(DataGetResponse);
                return true;
            }

            if (string.Equals(commandName, "ForcedDisconnect", StringComparison.Ordinal))
            {
                type = typeof(ForcedDisconnectResponse);
                return true;
            }

            if (string.Equals(commandName, "EventSubscribedMessage", StringComparison.Ordinal))
            {
                type = typeof(EventSubscribedMessage);
                return true;
            }

            type = null;
            return false;
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

            if (payloadObject.TryGetValue("error", StringComparison.OrdinalIgnoreCase, out var errorPayload) &&
                errorPayload.Type != JTokenType.String &&
                errorPayload.Type != JTokenType.Null &&
                HasStringMember(type, "error"))
            {
                payloadObject["error"] = errorPayload.Type == JTokenType.Object &&
                                         errorPayload["message"]?.Type == JTokenType.String
                    ? errorPayload["message"]!.ToString()
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

        private static bool TryDeserializeAsCommandError(string payloadJson, out CommandErrorResponse response)
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
                    error = errorToken["message"]!.ToString();
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
