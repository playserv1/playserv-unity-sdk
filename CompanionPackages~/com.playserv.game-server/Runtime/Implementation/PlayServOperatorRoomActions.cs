namespace Playserv.GameServer
{
    /// <summary>An operator removal. Complete the handler only after every game connection for this room/player is gone.</summary>
    public sealed class PlayServParticipantRemoveContext
    {
        internal PlayServParticipantRemoveContext(string requestId, string roomName, string playerId)
        { RequestId = requestId; RoomName = roomName; PlayerId = playerId; }
        /// <summary>Platform correlation ID, echoed by the SDK after the handler completes.</summary>
        public string RequestId { get; }
        /// <summary>The targeted logical room name, not its display title.</summary>
        public string RoomName { get; }
        /// <summary>The authenticated platform player to disconnect from this room.</summary>
        public string PlayerId { get; }
        /// <summary>Explicit operator cause; removal bypasses reconnect grace.</summary>
        public string Reason => "removed_by_operator";
    }

    /// <summary>An operator close, distinct from idle/lifetime expiry. Do not automatically reopen a fixed-name room.</summary>
    public sealed class PlayServOperatorRoomCloseContext
    {
        internal PlayServOperatorRoomCloseContext(string requestId, string roomName)
        { RequestId = requestId; RoomName = roomName; }
        /// <summary>Platform correlation ID, echoed by the SDK after the handler completes.</summary>
        public string RequestId { get; }
        /// <summary>The logical room to end, including all its game connections.</summary>
        public string RoomName { get; }
        /// <summary>Explicit operator cause, not an idle or lifetime expiry.</summary>
        public string Reason => "room_closed_by_operator";
    }
}
