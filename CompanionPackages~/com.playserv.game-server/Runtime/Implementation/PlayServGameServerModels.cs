using System;
using System.Collections.Generic;
using Playserv.Serialization;

namespace Playserv.GameServer
{
    /// <summary>Lifecycle state of one managed room registration.</summary>
    public enum PlayServGameRoomState
    {
        Active = 0,
        Degraded = 1,
        Draining = 2,
        Closed = 3,
        Terminated = 4
    }

    /// <summary>Immutable desired room state captured for upsert and heartbeat requests.</summary>
    public sealed class PlayServGameRoomSnapshot
    {
        private readonly string _attributesJson;

        public PlayServGameRoomSnapshot(
            string roomName, int players, int capacity, string state, object attributes, bool open, DateTimeOffset? createdAt)
            : this(roomName, players, capacity, state, attributes, open, createdAt, null, null) { }

        public PlayServGameRoomSnapshot(
            string roomName,
            int players,
            int capacity,
            string state = null,
            object attributes = null,
            bool open = true,
            DateTimeOffset? createdAt = null,
            PlayServGameRoomConnect connect = null,
            string region = null)
        {
            RoomName = roomName;
            Players = players;
            Capacity = capacity;
            State = state;
            Open = open;
            CreatedAt = createdAt;
            Connect = connect;
            Region = region;
            _attributesJson = PlayServGameServerJson.SerializeOptionalObject(attributes, nameof(attributes));
        }

        public string RoomName { get; }
        public int Players { get; }
        public int Capacity { get; }
        public string State { get; }
        public object Attributes => PlayServGameServerJson.ParseOptionalObject(_attributesJson);
        public bool Open { get; }
        public DateTimeOffset? CreatedAt { get; }
        public PlayServGameRoomConnect Connect { get; }
        public string Region { get; }

        internal PlayServGameRoomSnapshot WithCapacity(int capacity) => new PlayServGameRoomSnapshot(
            RoomName, Players, capacity, State, Attributes, Open, CreatedAt, Connect, Region);

        internal string AttributesJson => _attributesJson;
    }

    /// <summary>Initial registration request for a managed room.</summary>
    public sealed class PlayServStartRoomRequest
    {
        public PlayServStartRoomRequest(string functionSlug, PlayServGameRoomSnapshot snapshot)
        {
            FunctionSlug = functionSlug;
            Snapshot = snapshot;
        }

        public string FunctionSlug { get; }
        public PlayServGameRoomSnapshot Snapshot { get; }
    }

    /// <summary>Server-authorized matchmaking request for an explicit player.</summary>
    public sealed class PlayServServerFindMatchRequest
    {
        public string FunctionSlug { get; set; }
        public string PlayerId { get; set; }
        public string Matchmaker { get; set; }
        public object Parameters { get; set; }
        public int WaitMs { get; set; }
        public int SearchAgeMs { get; set; }
    }

    /// <summary>Short-lived room admission credential returned by matchmaking.</summary>
    public sealed class PlayServServerReservation
    {
        private readonly string _attributesJson;
        private readonly Func<double> _monotonicSeconds;
        private readonly double _receivedAt;

        internal PlayServServerReservation(string roomName, string token, DateTimeOffset expiresAt)
            : this(roomName, token, expiresAt, null, null, null, null, null, 0) { }

        internal PlayServServerReservation(
            string roomName, string token, DateTimeOffset expiresAt,
            PlayServGameRoomConnect connect, string region, object attributes,
            int? expiresIn, Func<double> monotonicSeconds, double receivedAt)
        {
            RoomName = roomName;
            Token = token;
            ExpiresAt = expiresAt;
            Connect = connect;
            Region = region;
            _attributesJson = PlayServGameServerJson.SerializeOptionalObject(attributes, nameof(attributes));
            ExpiresIn = expiresIn;
            _monotonicSeconds = monotonicSeconds;
            _receivedAt = receivedAt;
        }

        public string RoomName { get; }
        /// <summary>Secret admission ticket. Pass to the game transport; never log it.</summary>
        public string Token { get; }
        /// <summary>Server timestamp retained for compatibility; not a local expiry clock.</summary>
        public DateTimeOffset ExpiresAt { get; }
        /// <summary>Optional opaque endpoint; no DNS resolution or transport setup is performed.</summary>
        public PlayServGameRoomConnect Connect { get; }
        public string Region { get; }
        /// <summary>Optional JSON object. Each access returns a copy of the response snapshot.</summary>
        public object Attributes => PlayServGameServerJson.ParseOptionalObject(_attributesJson);
        /// <summary>Seconds remaining at response time; null means unknown on older backends.</summary>
        public int? ExpiresIn { get; }
        /// <summary>Advisory monotonic countdown, floored at zero. The backend remains authoritative for admission.</summary>
        public TimeSpan? RemainingLifetime => ExpiresIn.HasValue
            ? TimeSpan.FromSeconds(Math.Max(0, ExpiresIn.Value - Math.Max(0, _monotonicSeconds() - _receivedAt)))
            : (TimeSpan?)null;
    }

    /// <summary>Matchmaking placement result for a server caller.</summary>
    public sealed class PlayServServerMatchResult
    {
        internal PlayServServerMatchResult(
            string status,
            PlayServServerReservation reservation,
            int? retryAfterMs)
        {
            Status = status;
            Reservation = reservation;
            RetryAfterMs = retryAfterMs;
        }

        public string Status { get; }
        public PlayServServerReservation Reservation { get; }
        public int? RetryAfterMs { get; }
        public bool IsMatched => string.Equals(Status, "matched", StringComparison.Ordinal);
    }

    /// <summary>Accepted game-server deployment request.</summary>
    public sealed class PlayServServerLaunchResult
    {
        internal PlayServServerLaunchResult(string deploymentId, string region)
        {
            DeploymentId = deploymentId;
            Region = region;
        }

        public string DeploymentId { get; }
        public string Region { get; }
    }

    /// <summary>One backend room-list entry.</summary>
    public sealed class PlayServGameRoomInfo
    {
        internal PlayServGameRoomInfo(
            string roomName,
            int players,
            int capacity,
            int ageSeconds,
            string state,
            bool closing,
            string placementState,
            bool open,
            DateTimeOffset? drainUntil,
            string drainCause,
            PlayServGameRoomConnect connect = null,
            string region = null,
            string instanceId = null,
            object attributes = null)
        {
            RoomName = roomName;
            Players = players;
            Capacity = capacity;
            AgeSeconds = ageSeconds;
            State = state;
            Closing = closing;
            PlacementState = placementState;
            Open = open;
            DrainUntil = drainUntil;
            DrainCause = drainCause;
            Connect = connect; Region = region; InstanceId = instanceId;
            _attributesJson = PlayServGameServerJson.SerializeOptionalObject(attributes, nameof(attributes));
        }

        public string RoomName { get; }
        public int Players { get; }
        public int Capacity { get; }
        public int AgeSeconds { get; }
        public string State { get; }
        public bool Closing { get; }
        public string PlacementState { get; }
        public bool Open { get; }
        public DateTimeOffset? DrainUntil { get; }
        public string DrainCause { get; }
        private readonly string _attributesJson;
        public PlayServGameRoomConnect Connect { get; }
        public string Region { get; }
        public string InstanceId { get; }
        public object Attributes => PlayServGameServerJson.ParseOptionalObject(_attributesJson);
    }

    /// <summary>Backend placement state acknowledged by the latest room upsert.</summary>
    public sealed class PlayServRoomPlacementAcknowledgment
    {
        internal PlayServRoomPlacementAcknowledgment(
            string state,
            bool open,
            bool draining,
            string drainCause,
            DateTimeOffset? drainUntil,
            bool openRefused)
        {
            State = state;
            Open = open;
            Draining = draining;
            DrainCause = drainCause;
            DrainUntil = drainUntil;
            OpenRefused = openRefused;
        }

        public string State { get; }
        public bool Open { get; }
        public bool Draining { get; }
        public string DrainCause { get; }
        public DateTimeOffset? DrainUntil { get; }
        public bool OpenRefused { get; }

        internal bool ContentEquals(PlayServRoomPlacementAcknowledgment other)
        {
            return other != null &&
                   string.Equals(State, other.State, StringComparison.Ordinal) &&
                   Open == other.Open &&
                   Draining == other.Draining &&
                   string.Equals(DrainCause, other.DrainCause, StringComparison.Ordinal) &&
                   Nullable.Equals(DrainUntil, other.DrainUntil) &&
                   OpenRefused == other.OpenRefused;
        }
    }

    /// <summary>Result of a room registration or heartbeat.</summary>
    public sealed class PlayServRoomUpsertResult
    {
        internal PlayServRoomUpsertResult(bool created, PlayServRoomPlacementAcknowledgment placement)
        {
            Created = created;
            Placement = placement;
        }

        public bool Created { get; }
        public PlayServRoomPlacementAcknowledgment Placement { get; }
        public PlayServRoomConfiguration RoomConfiguration { get; internal set; }
        public string RosterCheck { get; internal set; }
    }

    /// <summary>Backend decision for a reservation admission attempt.</summary>
    public sealed class PlayServReservationConsumeResult
    {
        internal PlayServReservationConsumeResult(
            bool ok,
            string error,
            string roomName,
            string playerId,
            string errorCode)
        {
            Ok = ok;
            Error = error;
            RoomName = roomName;
            PlayerId = playerId;
            ErrorCode = errorCode;
        }

        public bool Ok { get; }
        public string Error { get; }
        public string RoomName { get; }
        public string PlayerId { get; }
        public string ErrorCode { get; }
    }

    /// <summary>Safe game-server view of a player without network or moderation signals.</summary>
    public sealed class PlayServGameServerPlayerProfile
    {
        internal PlayServGameServerPlayerProfile(
            string id,
            string name,
            string status,
            IReadOnlyList<string> sso,
            string country,
            string joined,
            DateTime createdAt,
            DateTime updatedAt)
        {
            Id = id;
            Name = name;
            Status = status;
            var providers = sso == null ? Array.Empty<string>() : new List<string>(sso).ToArray();
            Sso = Array.AsReadOnly(providers);
            Country = country;
            Joined = joined;
            CreatedAt = createdAt;
            UpdatedAt = updatedAt;
        }

        public string Id { get; }
        public string Name { get; }
        public string Status { get; }
        public IReadOnlyList<string> Sso { get; }
        public string Country { get; }
        public string Joined { get; }
        public DateTime CreatedAt { get; }
        public DateTime UpdatedAt { get; }
    }

    /// <summary>Remote-close outcome after the local room handle has stopped.</summary>
    public sealed class PlayServGameRoomCloseResult
    {
        internal PlayServGameRoomCloseResult(bool isSuccess, bool wasAlreadyClosed, Playserv.Wrapper.PlayServError error)
        {
            IsSuccess = isSuccess;
            WasAlreadyClosed = wasAlreadyClosed;
            Error = error ?? Playserv.Wrapper.PlayServError.None;
        }

        public bool IsSuccess { get; }
        public bool WasAlreadyClosed { get; }
        public Playserv.Wrapper.PlayServError Error { get; }
    }

    /// <summary>Per-room outcomes from graceful process shutdown.</summary>
    public sealed class PlayServGameServerShutdownResult
    {
        internal PlayServGameServerShutdownResult(
            IReadOnlyList<PlayServGameRoomCloseResult> rooms,
            Playserv.Wrapper.PlayServError analyticsError)
            : this(rooms, analyticsError, Playserv.Wrapper.PlayServError.None) { }

        internal PlayServGameServerShutdownResult(
            IReadOnlyList<PlayServGameRoomCloseResult> rooms,
            Playserv.Wrapper.PlayServError analyticsError,
            Playserv.Wrapper.PlayServError logsError)
        {
            var results = rooms == null
                ? Array.Empty<PlayServGameRoomCloseResult>()
                : new List<PlayServGameRoomCloseResult>(rooms).ToArray();
            Rooms = Array.AsReadOnly(results);
            AnalyticsError = analyticsError ?? Playserv.Wrapper.PlayServError.None;
            LogsError = logsError ?? Playserv.Wrapper.PlayServError.None;
        }

        public IReadOnlyList<PlayServGameRoomCloseResult> Rooms { get; }
        public Playserv.Wrapper.PlayServError AnalyticsError { get; }
        /// <summary>Best-effort log flush failure, independent of room-close and analytics results.</summary>
        public Playserv.Wrapper.PlayServError LogsError { get; }
        public bool IsSuccess
        {
            get
            {
                if (AnalyticsError.IsError || LogsError.IsError)
                    return false;

                for (var i = 0; i < Rooms.Count; i++)
                {
                    if (!Rooms[i].IsSuccess)
                        return false;
                }

                return true;
            }
        }
    }
}
