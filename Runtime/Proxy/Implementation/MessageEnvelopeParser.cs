using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Playserv.Proxy.Implementation
{
    internal static class MessageEnvelopeParser
    {
        public static MessageEnvelope Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("JSON string cannot be null or empty.", nameof(json));

            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException("Invalid message envelope JSON.", ex);
            }

            var command = string.Empty;
            if (root.TryGetValue("Command", StringComparison.OrdinalIgnoreCase, out var commandToken) &&
                commandToken != null &&
                commandToken.Type != JTokenType.Null)
            {
                command = commandToken.Type == JTokenType.String
                    ? commandToken.Value<string>() ?? string.Empty
                    : commandToken.ToString(Formatting.None);
            }

            var payload = string.Empty;
            if (root.TryGetValue("Payload", StringComparison.OrdinalIgnoreCase, out var payloadToken) &&
                payloadToken != null &&
                payloadToken.Type != JTokenType.Null)
            {
                payload = payloadToken.ToString(Formatting.None);
            }

            return new MessageEnvelope(command, payload);
        }
    }
}
