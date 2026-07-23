using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Playserv.Proxy.Logging;

namespace Playserv.Proxy.Implementation
{
    internal static class OutboundPacketDiagnostics
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, int> PacketCounts =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private static readonly ConditionalWeakTable<byte[], PacketMetadata> PacketMetadataByPayload =
            new ConditionalWeakTable<byte[], PacketMetadata>();

        private static long _windowStartedAt;

        public static string Register(MessageEnvelope envelope, object command, byte[] data)
        {
            var packetName = ResolvePacketName(envelope, command);
#if !PLAYSERV_DISABLE_LOGS
            if (data != null)
            {
                PacketMetadataByPayload.Remove(data);
                PacketMetadataByPayload.Add(data, new PacketMetadata(packetName));
            }
#endif
            return packetName;
        }

        public static bool BeginSocketWrite(byte[] data, ILogger logger)
        {
#if PLAYSERV_DISABLE_LOGS
            return false;
#else
            var packetName = ResolvePacketName(data);
            RecordPacket(packetName, logger);

            if (!string.Equals(packetName, "rpc:Respawn", StringComparison.OrdinalIgnoreCase))
                return false;

            logger?.LogWarning(
                $"[RESP-WIRE] enter utc={DateTime.UtcNow:HH:mm:ss.fff} bytes={data?.Length ?? 0}");
            return true;
#endif
        }

        public static void CompleteSocketWrite(bool isRespawn, ILogger logger)
        {
#if !PLAYSERV_DISABLE_LOGS
            if (isRespawn)
                logger?.LogWarning($"[RESP-WIRE] done utc={DateTime.UtcNow:HH:mm:ss.fff}");
#endif
        }

        internal static string ResolvePacketName(MessageEnvelope envelope, object command)
        {
            if (IsInvokeRpcCommand(envelope.Command) &&
                TryResolveMethodName(command, out var methodName))
            {
                return "rpc:" + methodName;
            }

            if (IsInvokeRpcCommand(envelope.Command) &&
                TryExtractJsonString(envelope.Payload, "MethodName", out methodName))
            {
                return "rpc:" + methodName;
            }

            return string.IsNullOrWhiteSpace(envelope.Command)
                ? "?"
                : envelope.Command;
        }

        internal static string ResolvePacketName(byte[] data)
        {
            if (data == null || data.Length == 0)
                return "?";

#if !PLAYSERV_DISABLE_LOGS
            if (PacketMetadataByPayload.TryGetValue(data, out var metadata))
            {
                PacketMetadataByPayload.Remove(data);
                return metadata.PacketName;
            }
#endif

            try
            {
                var json = Encoding.UTF8.GetString(data);
                if (TryExtractJsonString(json, "MethodName", out var methodName) ||
                    TryExtractJsonString(json, "methodName", out methodName) ||
                    TryExtractJsonString(json, "method", out methodName))
                {
                    return "rpc:" + methodName;
                }

                if (TryExtractJsonString(json, "Command", out var command) ||
                    TryExtractJsonString(json, "command", out command) ||
                    TryExtractJsonString(json, "EventType", out command) ||
                    TryExtractJsonString(json, "eventType", out command) ||
                    TryExtractJsonString(json, "type", out command))
                {
                    return command;
                }
            }
            catch
            {
                // Diagnostics must never affect transport delivery.
            }

            return "?";
        }

        private static void RecordPacket(string packetName, ILogger logger)
        {
            lock (Gate)
            {
                var now = Stopwatch.GetTimestamp();
                if (_windowStartedAt == 0)
                    _windowStartedAt = now;

                PacketCounts.TryGetValue(packetName, out var count);
                PacketCounts[packetName] = count + 1;

                var elapsedMilliseconds =
                    (now - _windowStartedAt) * 1000.0 / Stopwatch.Frequency;
                if (elapsedMilliseconds < 1000.0)
                    return;

                var packetNames = new List<string>(PacketCounts.Keys);
                packetNames.Sort(StringComparer.Ordinal);
                var summary = new StringBuilder();
                for (var i = 0; i < packetNames.Count; i++)
                {
                    if (summary.Length > 0)
                        summary.Append(' ');

                    var name = packetNames[i];
                    summary.Append(name)
                        .Append('=')
                        .Append(PacketCounts[name]);
                }

                logger?.LogWarning($"[PKT-OUT] utc={DateTime.UtcNow:HH:mm:ss} {summary}");
                PacketCounts.Clear();
                _windowStartedAt = now;
            }
        }

        private static bool IsInvokeRpcCommand(string commandName)
        {
            if (string.IsNullOrWhiteSpace(commandName))
                return false;

            return string.Equals(commandName, "InvokeRpc", StringComparison.OrdinalIgnoreCase) ||
                   commandName.EndsWith(".InvokeRpc", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryResolveMethodName(object command, out string methodName)
        {
            methodName = null;
            if (command == null)
                return false;

            try
            {
                var property = command.GetType().GetProperty(
                    "MethodName",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
                methodName = property?.GetValue(command) as string;
                return !string.IsNullOrWhiteSpace(methodName);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryExtractJsonString(string json, string key, out string value)
        {
            value = null;
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key))
                return false;

            var token = "\"" + key + "\"";
            var tokenIndex = json.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (tokenIndex < 0)
                return false;

            var colonIndex = json.IndexOf(':', tokenIndex + token.Length);
            if (colonIndex < 0)
                return false;

            var valueStart = colonIndex + 1;
            while (valueStart < json.Length && char.IsWhiteSpace(json[valueStart]))
                valueStart++;

            if (valueStart >= json.Length || json[valueStart] != '"')
                return false;

            valueStart++;
            var valueEnd = json.IndexOf('"', valueStart);
            if (valueEnd < 0)
                return false;

            value = json.Substring(valueStart, valueEnd - valueStart);
            return !string.IsNullOrWhiteSpace(value);
        }

        private sealed class PacketMetadata
        {
            public PacketMetadata(string packetName)
            {
                PacketName = packetName;
            }

            public string PacketName { get; }
        }
    }
}
