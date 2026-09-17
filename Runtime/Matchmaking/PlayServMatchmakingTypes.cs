using System;
using System.Collections.Generic;
using Playserv.Wrapper;

namespace Playserv.Matchmaking
{
    /// <summary>
    /// One player-authenticated placement request. <see cref="Parameters"/>
    /// is forwarded as the backend's free-form <c>params</c> lobby-state object.
    /// </summary>
    public sealed class PlayServFindMatchRequest
    {
        public string FunctionSlug { get; set; }

        public string Matchmaker { get; set; }

        /// <summary>A typed DTO, anonymous object, or string-keyed dictionary.</summary>
        public object Parameters { get; set; }

        public int WaitMs { get; set; }

        public int SearchAgeMs { get; set; }
    }

    /// <summary>
    /// Options for the SDK-managed matchmaking wait loop. The SDK derives
    /// <c>search_age_ms</c> from elapsed time and carries the same parameter
    /// snapshot across every poll.
    /// </summary>
    public sealed class PlayServJoinGameRequest
    {
        public const int DefaultWaitMs = 20_000;

        public string FunctionSlug { get; set; }

        public string Matchmaker { get; set; }

        /// <summary>A typed DTO, anonymous object, or string-keyed dictionary.</summary>
        public object Parameters { get; set; }

        public int WaitMs { get; set; } = DefaultWaitMs;
    }

    /// <summary>
    /// An orchestrator deployment accepted by PlayServ. The room is not
    /// available until the launched server registers itself.
    /// </summary>
    public sealed class PlayServServerLaunchResult
    {
        internal PlayServServerLaunchResult(string deploymentId, string region)
        {
            DeploymentId = deploymentId ?? string.Empty;
            Region = region;
        }

        public string DeploymentId { get; }

        public string Region { get; }
    }

    /// <summary>A room reservation minted by the PlayServ matchmaking runtime.</summary>
    public sealed class PlayServMatchReservation
    {
        private readonly Func<double> _monotonicSeconds;
        private readonly double _receivedAt;

        internal PlayServMatchReservation(
            string roomName,
            string reservationToken,
            DateTimeOffset expiresAt)
            : this(roomName, reservationToken, expiresAt, null, null, null, null, null, 0)
        {
        }

        internal PlayServMatchReservation(
            string roomName, string reservationToken, DateTimeOffset expiresAt,
            PlayServRoomConnect connect, string region, IReadOnlyDictionary<string, object> attributes,
            int? expiresIn, Func<double> monotonicSeconds, double receivedAt)
        {
            RoomName = roomName ?? string.Empty;
            ReservationToken = reservationToken ?? string.Empty;
            ExpiresAt = expiresAt;
            Connect = connect;
            Region = region;
            Attributes = attributes;
            ExpiresIn = expiresIn;
            _monotonicSeconds = monotonicSeconds;
            _receivedAt = receivedAt;
        }

        public string RoomName { get; }

        /// <summary>Secret admission ticket. Pass to the game transport; never log it.</summary>
        public string ReservationToken { get; }

        /// <summary>Server timestamp for compatibility, not a local expiry clock.</summary>
        public DateTimeOffset ExpiresAt { get; }

        public PlayServRoomConnect Connect { get; }
        public string Region { get; }
        public IReadOnlyDictionary<string, object> Attributes { get; }

        /// <summary>Seconds remaining at response time; null on older backends.</summary>
        public int? ExpiresIn { get; }

        /// <summary>
        /// Local monotonic countdown, floored at zero. Null means unknown, not unlimited.
        /// This is advisory; the server remains authoritative for admission.
        /// </summary>
        public TimeSpan? RemainingLifetime => ExpiresIn.HasValue
            ? TimeSpan.FromSeconds(Math.Max(0, ExpiresIn.Value - Math.Max(0, _monotonicSeconds() - _receivedAt)))
            : (TimeSpan?)null;
    }

    public enum PlayServMatchStatus
    {
        Matched,
        Searching,
        NotFound,
        Bot
    }

    public sealed class PlayServMatchResult
    {
        internal PlayServMatchResult(
            PlayServMatchStatus status,
            PlayServMatchReservation reservation,
            int? retryAfterMs)
        {
            Status = status;
            Reservation = reservation;
            RetryAfterMs = retryAfterMs;
        }

        public PlayServMatchStatus Status { get; }

        public PlayServMatchReservation Reservation { get; }

        public int? RetryAfterMs { get; }

        public bool IsMatched => Status == PlayServMatchStatus.Matched;
    }

    public enum PlayServJoinGameStatus
    {
        Matched,
        NotFound,
        Bot
    }

    public sealed class PlayServJoinGameResult
    {
        internal PlayServJoinGameResult(
            PlayServJoinGameStatus status,
            PlayServMatchReservation reservation)
        {
            Status = status;
            Reservation = reservation;
        }

        public PlayServJoinGameStatus Status { get; }

        public PlayServMatchReservation Reservation { get; }
    }

    /// <summary>
    /// Throw this from the optional room-entry callback when the room refuses
    /// the reservation. A <c>room_closed</c> refusal automatically restarts
    /// placement up to three times.
    /// </summary>
    public sealed class PlayServRoomEntryRefusedException : Exception
    {
        public PlayServRoomEntryRefusedException(string errorCode)
            : base($"Room entry refused: {errorCode}")
        {
            ErrorCode = errorCode ?? string.Empty;
            UnifiedError = new PlayServError(
                PlayServErrorCode.Conflict,
                ErrorCode,
                Message,
                retryable: string.Equals(ErrorCode, "room_closed", StringComparison.Ordinal));
        }

        public string ErrorCode { get; }

        /// <summary>Normalized representation while preserving <see cref="ErrorCode"/>.</summary>
        public PlayServError UnifiedError { get; }

    }

    public enum PlayServMatchmakingOperation
    {
        FindMatch,
        JoinGame,
        LaunchServer,
        BrowseRooms,
        JoinRoom,
        HostRoom
    }

    /// <summary>Known room refusals; unknown backend codes remain in UnifiedError.SourceCode.</summary>
    public enum PlayServRoomFailureCode
    {
        Unknown,
        RoomNotFound,
        RoomFull,
        RoomClosed,
        RoomTypeNotFound,
        RoomRefused,
        RoomUnreachable,
        RoomQuotaExceeded,
        RoomHostUnavailable,
        RoomHostCapacityExhausted,
        RegionUnavailable
    }

    /// <summary>Normalized operational failure from player matchmaking.</summary>
    public sealed class PlayServMatchmakingException : Exception
    {
        internal PlayServMatchmakingException(
            PlayServMatchmakingOperation operation,
            string functionSlug,
            PlayServError error,
            Exception innerException = null)
            : base(error?.Message ?? "PlayServ matchmaking failed.", innerException)
        {
            Operation = operation;
            FunctionSlug = functionSlug ?? string.Empty;
            UnifiedError = error ?? new PlayServError(
                PlayServErrorCode.Unknown,
                "matchmaking_unknown",
                "PlayServ matchmaking failed.");
        }

        public PlayServMatchmakingOperation Operation { get; }

        public string FunctionSlug { get; }

        public PlayServError UnifiedError { get; }

        /// <summary>Optional server retry delay; does not enable low-level automatic retries.</summary>
        public TimeSpan? RetryAfter { get; internal set; }

        /// <summary>Typed room refusal without changing the shared error category.</summary>
        public PlayServRoomFailureCode RoomFailureCode => MapRoomFailure(UnifiedError.SourceCode);

        internal static PlayServRoomFailureCode MapRoomFailure(string code)
        {
            switch (code)
            {
                case "room_not_found": return PlayServRoomFailureCode.RoomNotFound;
                case "room_full": return PlayServRoomFailureCode.RoomFull;
                case "room_closed": return PlayServRoomFailureCode.RoomClosed;
                case "room_type_not_found": return PlayServRoomFailureCode.RoomTypeNotFound;
                case "room_refused": return PlayServRoomFailureCode.RoomRefused;
                case "room_unreachable": return PlayServRoomFailureCode.RoomUnreachable;
                case "room_quota_exceeded": return PlayServRoomFailureCode.RoomQuotaExceeded;
                case "room_host_unavailable": return PlayServRoomFailureCode.RoomHostUnavailable;
                case "room_host_capacity_exhausted": return PlayServRoomFailureCode.RoomHostCapacityExhausted;
                case "region_unavailable": return PlayServRoomFailureCode.RegionUnavailable;
                default: return PlayServRoomFailureCode.Unknown;
            }
        }
    }
}
