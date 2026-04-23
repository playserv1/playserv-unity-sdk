using System;
using System.Collections.Generic;
using Playserv.Serialization;

namespace Playserv.Proxy.Implementation
{
    internal sealed class MessageEnvelopeCodec
    {
        private readonly IJsonCodec _jsonCodec;

        public MessageEnvelopeCodec(IJsonCodec jsonCodec)
        {
            _jsonCodec = jsonCodec ?? throw new ArgumentNullException(nameof(jsonCodec));
        }

        public string Serialize(MessageEnvelope envelope)
        {
            var payloadValue = string.IsNullOrWhiteSpace(envelope.Payload)
                ? null
                : _jsonCodec.ParseToPlainValue(envelope.Payload);

            var document = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["Command"] = envelope.Command ?? string.Empty,
                ["Payload"] = payloadValue
            };

            return _jsonCodec.Serialize(document);
        }

        public MessageEnvelope Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("JSON string cannot be null or empty.", nameof(json));

            var document = _jsonCodec.ParseToPlainValue(json);
            if (document is not IDictionary<string, object> payload)
                throw new InvalidOperationException("Invalid message envelope JSON.");

            var command = string.Empty;
            if (_jsonCodec.TryGetProperty(payload, "Command", ignoreCase: true, out var commandValue) &&
                commandValue != null)
            {
                command = commandValue is string text
                    ? text
                    : _jsonCodec.Serialize(commandValue);
            }

            var messagePayload = string.Empty;
            if (_jsonCodec.TryGetProperty(payload, "Payload", ignoreCase: true, out var payloadValue) &&
                payloadValue != null)
            {
                messagePayload = _jsonCodec.Serialize(payloadValue);
            }

            return new MessageEnvelope(command, messagePayload);
        }
    }
}
