using System;
using Playserv.Serialization;

namespace Playserv.GameServer
{
    [Serializable]
    internal sealed class FindMatchRequestWire
    {
        public string player_id;
        public string matchmaker;

        [PlayServJsonName("params")]
        public object parameters;

        public int wait_ms;
        public int search_age_ms;
    }

    [Serializable]
    internal sealed class FindMatchResponseWire
    {
        public string status;
        public string room_name;
        public string reservation_token;
        public DateTimeOffset? expires_at;
        public int? expires_in;
        public RoomConnectWire connect;
        public string region;
        public object attributes;
        public int? retry_after_ms;
    }

    [Serializable]
    internal sealed class LaunchServerRequestWire
    {
        public string region;
    }

    [Serializable]
    internal sealed class LaunchServerResponseWire
    {
        public string deployment_id;
        public string region;
    }

    [Serializable]
    internal sealed class UpsertRoomRequestWire
    {
        public string room_name;
        public int players;
        public int capacity;
        public string state;
        public object attributes;
        public bool? open;
        public DateTimeOffset? created_at;
        public object connect;
        public string region;
        public string instance_id;
        public string roster_hash;
        public int? roster_count;
    }

    [Serializable]
    internal sealed class UpsertRoomResponseWire
    {
        public bool created;
        public PlacementWire placement;
        public RoomConfigWire room_config;
        public string roster_check;
    }

    [Serializable]
    internal sealed class PlacementWire
    {
        public string state;
        public bool open;
        public bool draining;
        public string drain_cause;
        public DateTimeOffset? drain_until;
        public bool open_refused;
    }

    [Serializable]
    internal sealed class RoomListItemWire
    {
        public RoomConnectWire connect;
        public string region;
        public string instance_id;
        public object attributes;
        public string room_name;
        public int players;
        public int capacity;
        public int age_sec;
        public string state;
        public bool closing;
        public string placement_state;
        public bool open;
        public DateTimeOffset? drain_until;
        public string drain_cause;
    }

    [Serializable]
    internal sealed class ConsumeReservationRequestWire
    {
        public string player_id;
        public string room_name;
    }

    [Serializable]
    internal sealed class ConsumeReservationResponseWire
    {
        public bool ok;
        public string error;
        public string room_name;
        public string player_id;
        public string error_code;
    }

    [Serializable]
    internal sealed class PlayerProfileWire
    {
        public string id;
        public string name;
        public string status;
        public string[] sso;
        public string country;
        public string joined;
        public DateTime created_at;
        public DateTime updated_at;
    }

    [Serializable]
    internal sealed class ProblemDetailsWire
    {
        public string code;
        public string error;
        public string title;
        public string detail;
        public bool? retryable;
    }
}
