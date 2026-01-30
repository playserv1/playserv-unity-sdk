using System;
using Newtonsoft.Json;
using Playserv.Proxy.Interfaces;

namespace Playserv.Proxy.Implementation
{
    public sealed class JsonSerializer : IMessageSerializer
    {
        public MessageEnvelope Serialize<T>(T command, string moduleName = null)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));

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
            var dotIndex = commandName.IndexOf('.');
            if (dotIndex >= 0 && dotIndex < commandName.Length - 1)
            {
                commandName = commandName.Substring(dotIndex + 1);
            }

            var type = FindType(commandName);
            
            if (type == null)
                throw new InvalidOperationException($"Unknown command type: {envelope.Command}");

            var cmd = JsonConvert.DeserializeObject(envelope.Payload, type);
            
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