using System;
using Playserv.DataSubscription.Responses;
using Playserv.Events.Requests;
using Playserv.Events.Responses;
using Playserv.RPC;
using Playserv.Proxy.Common;
using Playserv.Proxy.Interfaces;
using Playserv.Serialization;

namespace Playserv.Proxy.Implementation
{
    public sealed class JsonSerializer : IMessageSerializer
    {
        private readonly IJsonCodec _jsonCodec;
        private readonly ICommandPayloadMapper _commandPayloadMapper;

        public JsonSerializer()
            : this(new NewtonsoftJsonCodec(), new NewtonsoftCommandPayloadMapper())
        {
        }

        public JsonSerializer(IJsonCodec jsonCodec, ICommandPayloadMapper commandPayloadMapper)
        {
            _jsonCodec = jsonCodec ?? throw new ArgumentNullException(nameof(jsonCodec));
            _commandPayloadMapper = commandPayloadMapper ?? throw new ArgumentNullException(nameof(commandPayloadMapper));
        }

        public MessageEnvelope Serialize<T>(T command, string moduleName = null)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            if (command is EventMessage eventMessage &&
                string.Equals(eventMessage.EventType, "KeepAlive", StringComparison.OrdinalIgnoreCase))
            {
                var keepAlivePayload = _commandPayloadMapper.BuildKeepAlivePayloadJson(eventMessage.EventType, eventMessage.Payload);
                return new MessageEnvelope("EventMessage", keepAlivePayload);
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

            var payloadJson = _jsonCodec.Serialize(command);

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

            var normalizedPayload = _commandPayloadMapper.NormalizePayloadForType(envelope.Payload, type);
            object cmd;
            try
            {
                cmd = _jsonCodec.Deserialize(normalizedPayload, type);
            }
            catch (JsonCodecException)
            {
                if (_commandPayloadMapper.TryDeserializeAsCommandError(envelope.Payload, out var fallback))
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
