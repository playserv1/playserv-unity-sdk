using System;
using Playserv.Events.Requests;
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

        internal IJsonCodec JsonCodec => _jsonCodec;

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

            var type = ResolveCommandType(envelope.Command);
            
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

        private static Type ResolveCommandType(string commandName)
        {
            if (CommandTypeRegistry.TryResolve(commandName, out var type))
                return type;

            return null;
        }
    }
}
