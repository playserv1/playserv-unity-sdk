using System;

namespace Playserv.Proxy.Implementation
{
    internal static class MessageEnvelopeParser
    {
        public static MessageEnvelope Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("JSON string cannot be null or empty.", nameof(json));

            var trimmed = json.Trim();
            if (!trimmed.StartsWith("{") || !trimmed.EndsWith("}"))
                throw new InvalidOperationException("Invalid JSON format.");

            var command = ExtractStringValue(json, "Command");
            var payloadJson = ExtractPayloadValue(json);

            return new MessageEnvelope(command, payloadJson);
        }

        private static string ExtractStringValue(string json, string key)
        {
            var keyPattern = $"\"{key}\"";
            var keyIndex = json.IndexOf(keyPattern, StringComparison.OrdinalIgnoreCase);
            if (keyIndex < 0)
                return string.Empty;

            var valueStart = json.IndexOf(':', keyIndex + keyPattern.Length);
            if (valueStart < 0)
                return string.Empty;

            var quoteStart = json.IndexOf('"', valueStart);
            if (quoteStart < 0)
                return string.Empty;

            var quoteEnd = json.IndexOf('"', quoteStart + 1);
            if (quoteEnd < 0)
                return string.Empty;

            return json.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
        }

        private static string ExtractPayloadValue(string json)
        {
            var keyPattern = "\"Payload\"";
            var keyIndex = json.IndexOf(keyPattern, StringComparison.OrdinalIgnoreCase);
            if (keyIndex < 0)
                return string.Empty;

            var valueStart = json.IndexOf(':', keyIndex + keyPattern.Length);
            if (valueStart < 0)
                return string.Empty;

            valueStart++;
            while (valueStart < json.Length && char.IsWhiteSpace(json[valueStart]))
                valueStart++;

            if (valueStart >= json.Length)
                return string.Empty;

            if (json[valueStart] == 'n' && json.Substring(valueStart, 4) == "null")
                return string.Empty;

            if (json[valueStart] == '"')
            {
                var endQuote = json.IndexOf('"', valueStart + 1);
                if (endQuote < 0)
                    return string.Empty;
                return json.Substring(valueStart, endQuote - valueStart + 1);
            }

            if (json[valueStart] == '{')
            {
                var depth = 0;
                var i = valueStart;
                while (i < json.Length)
                {
                    if (json[i] == '{')
                        depth++;
                    else if (json[i] == '}')
                    {
                        depth--;
                        if (depth == 0)
                            return json.Substring(valueStart, i - valueStart + 1);
                    }
                    i++;
                }
            }

            if (json[valueStart] == '[')
            {
                var depth = 0;
                var i = valueStart;
                while (i < json.Length)
                {
                    if (json[i] == '[')
                        depth++;
                    else if (json[i] == ']')
                    {
                        depth--;
                        if (depth == 0)
                            return json.Substring(valueStart, i - valueStart + 1);
                    }
                    i++;
                }
            }

            return string.Empty;
        }
    }
}