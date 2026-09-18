using System.Collections.Generic;

namespace Playserv.Matchmaking
{
    /// <summary>Player room-browser filters, copied when the call starts.</summary>
    public sealed class PlayServRoomBrowseQuery
    {
        /// <summary>open, full, self_closed, draining or session_closing; null means no filter.</summary>
        public string PlacementState { get; set; }
        public string Region { get; set; }
        /// <summary>Up to four exact attribute filters, expressed in their wire string form.</summary>
        public IReadOnlyDictionary<string, string> Attributes { get; set; }
        public string Cursor { get; set; }
        /// <summary>Page size, 1–200; defaults to 50.</summary>
        public int Limit { get; set; } = 50;
    }

    /// <summary>Endpoint metadata returned unchanged by matchmaking. Connecting requires an explicit game-owned action.</summary>
    public sealed class PlayServRoomConnect
    {
        internal PlayServRoomConnect(string host, int port, string transport, string connectString, string region)
        {
            Host = host;
            Port = port;
            Transport = transport;
            ConnectString = connectString;
            Region = region;
        }

        public string Host { get; }
        public int Port { get; }
        public string Transport { get; }
        public string ConnectString { get; }
        public string Region { get; }
    }

    /// <summary>Public room snapshot; no server drain or instance metadata.</summary>
    public sealed class PlayServRoomListing
    {
        internal PlayServRoomListing(string roomName, int players, int capacity, string state,
            string placementState, string region, IReadOnlyDictionary<string, object> attributes,
            PlayServRoomConnect connect)
        {
            RoomName = roomName;
            Players = players;
            Capacity = capacity;
            State = state;
            PlacementState = placementState;
            Region = region;
            Attributes = attributes;
            Connect = connect;
        }

        public string RoomName { get; }
        /// <summary>May exceed Capacity after a capacity shrink; not a local admission check.</summary>
        public int Players { get; }
        public int Capacity { get; }
        public string State { get; }
        /// <summary>Preserves the wire value, including session_closing and future states.</summary>
        public string PlacementState { get; }
        public string Region { get; }
        public IReadOnlyDictionary<string, object> Attributes { get; }
        /// <summary>Null when the room has not declared an endpoint yet.</summary>
        public PlayServRoomConnect Connect { get; }
    }

    public sealed class PlayServRoomBrowsePage
    {
        internal PlayServRoomBrowsePage(IReadOnlyList<PlayServRoomListing> rooms, string cursorNext, bool hasMore)
        {
            Rooms = rooms;
            CursorNext = cursorNext;
            HasMore = hasMore;
        }

        public IReadOnlyList<PlayServRoomListing> Rooms { get; }
        /// <summary>Opaque cursor to pass in the next query; null at the end.</summary>
        public string CursorNext { get; }
        public bool HasMore { get; }
    }
}
