using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Playserv.Serialization;

namespace Playserv.GameServer
{
    /// <summary>Explicit, provider-neutral launch metadata. Does not start rooms, resolve DNS or open a game transport.</summary>
    public static class PlayServServerLaunch
    {
        public static string GetRoomName(string roomName = null) => GetRoomName(roomName, Environment.GetEnvironmentVariable);
        internal static string GetRoomName(string roomName, Func<string, string> environment)
        {
            var value = roomName ?? environment("PLAYSERV_ROOM_NAME");
            if (value == null || roomName == null && string.IsNullOrWhiteSpace(value)) return null;
            if (!Regex.IsMatch(value, "\\A[A-Za-z0-9][A-Za-z0-9:._-]{0,63}\\z"))
                throw new ArgumentException("Launch room name is invalid.", nameof(roomName));
            return value;
        }

        /// <summary>Explicit connect wins. Missing public address returns null; malformed metadata is rejected without exposing its value.</summary>
        public static PlayServGameRoomConnect ResolveConnect(int listenPort, PlayServGameRoomConnect connect = null) =>
            ResolveConnect(listenPort, connect, Environment.GetEnvironmentVariable);

        internal static PlayServGameRoomConnect ResolveConnect(int listenPort, PlayServGameRoomConnect connect, Func<string, string> environment)
        {
            if (connect != null) return connect;
            if (listenPort < 1 || listenPort > 65535) throw new ArgumentOutOfRangeException(nameof(listenPort));
            var host = environment("PLAYSERV_PUBLIC_IP");
            var mapping = environment("PLAYSERV_PORTS_MAPPING");
            var port = listenPort; var transport = "udp";
            try
            {
                if (!string.IsNullOrWhiteSpace(mapping))
                {
                    var root = new NewtonsoftJsonCodec().ParseToPlainValue(mapping);
                    if (root is IDictionary<string, object> keyed)
                    {
                        foreach (var suffix in new[] { "", "/udp", "/tcp" })
                        {
                            if (!keyed.TryGetValue(listenPort.ToString(CultureInfo.InvariantCulture) + suffix, out var value)) continue;
                            port = ReadPort(value); transport = suffix == "/tcp" ? "tcp" : "udp"; break;
                        }
                    }
                    else if (root is IList entries)
                    {
                        foreach (var item in entries)
                        {
                            if (!(item is IDictionary<string, object> entry) || !entry.TryGetValue("internal_port", out var inside)) throw new FormatException();
                            if (ReadPort(inside) != listenPort) continue;
                            if (!entry.TryGetValue("external_port", out var outside)) throw new FormatException();
                            port = ReadPort(outside);
                            if (entry.TryGetValue("protocol", out var protocol))
                            {
                                if (!(protocol is string text) || text != "udp" && text != "tcp") throw new FormatException();
                                transport = text;
                            }
                            break;
                        }
                    }
                    else throw new FormatException();
                }
                if (string.IsNullOrWhiteSpace(host)) return null;
                host = host.Trim();
                if (host.Length > 253 || host.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)) ||
                    host.IndexOfAny(new[] { '/', '?', '#', '@', '\\' }) >= 0) throw new FormatException();
                return new PlayServGameRoomConnect(host, port, transport);
            }
            catch { throw new ArgumentException("Launch endpoint metadata is invalid.", nameof(environment)); }
        }
        private static int ReadPort(object value)
        {
            if (value is IDictionary<string, object> entry && entry.TryGetValue("external_port", out var child)) return ReadPort(child);
            long port;
            if (value is long large) port = large;
            else if (value is int small) port = small;
            else if (value is string text && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)) port = parsed;
            else throw new FormatException();
            if (port < 1 || port > 65535) throw new FormatException();
            return (int)port;
        }
    }
}
