namespace Playserv.Matchmaking
{
    /// <summary>One direct room join. Player identity is supplied only by the authenticated session.</summary>
    public sealed class PlayServJoinRoomRequest
    {
        public string FunctionSlug { get; set; }
        public string RoomName { get; set; }
        /// <summary>Optional JSON object passed to the admission hook, snapshotted when the call begins. Do not log it.</summary>
        public object Params { get; set; }
    }
}
