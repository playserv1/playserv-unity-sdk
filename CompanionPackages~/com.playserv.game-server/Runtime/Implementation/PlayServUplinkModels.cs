using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Wrapper;

namespace Playserv.GameServer
{
    /// <summary>Process connection state; a terminal refusal requires explicit disconnect/reconfiguration.</summary>
    public enum PlayServUplinkState { Disconnected, Connecting, Connected, Reconnecting, Terminated }

    /// <summary>Resolves an upgrade credential, either a deployment JWT or a studio server key. Never persisted.</summary>
    public interface IPlayServUplinkCredentialProvider
    {
        Task<string> GetCredentialAsync(CancellationToken ct = default);
    }

    /// <summary>Platform-owned room facts. Only a strictly newer version replaces the current configuration.</summary>
    public sealed class PlayServRoomConfiguration
    {
        internal PlayServRoomConfiguration(RoomConfigWire wire)
        {
            Capacity = wire.capacity; ReservationTtlSeconds = wire.reservation_ttl_seconds;
            RoomLifetimeSeconds = wire.room_lifetime_seconds; RoomIdleTimeoutSeconds = wire.room_idle_timeout_seconds;
            MaxRooms = wire.max_rooms; Version = wire.version;
        }
        public int Capacity { get; }
        public int ReservationTtlSeconds { get; }
        public int RoomLifetimeSeconds { get; }
        public int? RoomIdleTimeoutSeconds { get; }
        public int MaxRooms { get; }
        public long Version { get; }
    }

    /// <summary>Opaque game networking endpoint; the SDK does not resolve hosts or initialize a networking framework.</summary>
    public sealed class PlayServGameRoomConnect
    {
        public PlayServGameRoomConnect(string host = null, int? port = null, string transport = null, string connectString = null)
        {
            if (port.HasValue && (port < 1 || port > 65535)) throw new ArgumentOutOfRangeException(nameof(port));
            Host = host; Port = port; Transport = transport; ConnectString = connectString;
        }
        public string Host { get; }
        public int? Port { get; }
        public string Transport { get; }
        public string ConnectString { get; }
        internal object ToWire() => new { host = Host, port = Port, transport = Transport, connect_string = ConnectString };
    }

    [Serializable] internal sealed class RoomConfigWire
    {
        public int capacity;
        public int reservation_ttl_seconds;
        public int room_lifetime_seconds;
        public int? room_idle_timeout_seconds;
        public int max_rooms;
        public long version;
    }
    [Serializable] internal sealed class RoomConnectWire
    {
        public string host; public int? port; public string transport; public string connect_string;
        internal PlayServGameRoomConnect ToModel() => new PlayServGameRoomConnect(host, port, transport, connect_string);
    }
    [Serializable] internal sealed class UplinkTypeWire { public string type; }
    [Serializable] internal sealed class UplinkFrameWire
    {
        public string type;
        public string session_token;
        public int? expires_in;
        public string admission;
        public RoomConfigWire room_config;
    }

    internal static class UplinkErrors
    {
        internal static PlayServGameServerException Exception(string code, bool retryable = false) =>
            new PlayServGameServerException(new PlayServError(
                code == "uplink_unauthorized" ? PlayServErrorCode.Unauthorized : PlayServErrorCode.Transport,
                code, "The PlayServ server uplink could not complete the operation.", retryable: retryable));
    }
}
