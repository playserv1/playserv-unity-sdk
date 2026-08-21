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
            string roomName,
            int players,
            int capacity,
            string state = null,
            object attributes = null,
            bool open = true,
            DateTimeOffset? createdAt = null)
        {
            RoomName = roomName;
            Players = players;
            Capacity = capacity;
            State = state;
            Open = open;
            CreatedAt = createdAt;
            _attributesJson = PlayServGameServerJson.SerializeOptionalObject(attributes, nameof(attributes));
        }

        public string RoomName { get; }
        public int Players { get; }
        public int Capacity { get; }
        public string State { get; }
        public object Attributes => PlayServGameServerJson.ParseOptionalObject(_attributesJson);
        public bool Open { get; }
        public DateTimeOffset? CreatedAt { get; }

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
        internal PlayServServerReservation(string roomName, string token, DateTimeOffset expiresAt)
        {
            RoomName = roomName;
            Token = token;
            ExpiresAt = expiresAt;
        }

        public string RoomName { get; }
        public string Token { get; }
        public DateTimeOffset ExpiresAt { get; }
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
            string drainCause)
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
        {
            var results = rooms == null
                ? Array.Empty<PlayServGameRoomCloseResult>()
                : new List<PlayServGameRoomCloseResult>(rooms).ToArray();
            Rooms = Array.AsReadOnly(results);
            AnalyticsError = analyticsError ?? Playserv.Wrapper.PlayServError.None;
        }

        public IReadOnlyList<PlayServGameRoomCloseResult> Rooms { get; }
        public Playserv.Wrapper.PlayServError AnalyticsError { get; }
        public bool IsSuccess
        {
            get
            {
                if (AnalyticsError.IsError)
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
