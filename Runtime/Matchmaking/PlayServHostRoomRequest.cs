using System;

namespace Playserv.Matchmaking
{
    /// <summary>Asks an already connected server to host a room. The platform mints its name.</summary>
    public sealed class PlayServHostRoomRequest
    {
        /// <summary>Room-type slug, not a caller-selected room name.</summary>
        public string FunctionSlug { get; set; }

        /// <summary>
        /// Optional flat JSON-object DTO or dictionary, at most 2048 UTF-8 bytes.
        /// A wish the server may alter or refuse; response attributes are authoritative.
        /// Snapshotted before authentication or HTTP can yield.
        /// </summary>
        public object Attributes { get; set; }

        /// <summary>Optional platform region, at most 32 characters.</summary>
        public string Region { get; set; }
    }

    /// <summary>Budget for a single Host request. Does not enable retries, polling or travel.</summary>
    public sealed class PlayServRoomHostOptions
    {
        /// <summary>
        /// Total authentication and HTTP budget, default 45 seconds. Must be positive
        /// and no greater than Int32.MaxValue milliseconds. Cancellation or timeout
        /// after sending does not roll back room creation; repeating Host can create another room.
        /// </summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(45);
    }
}
