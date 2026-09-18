using System;
using System.Collections.Generic;
using System.Linq;
using Playserv.Http.Interfaces;

namespace Playserv.Wrapper
{
    public enum PlayServExternalLoginMode
    {
        PreserveCurrentPlayer = 0,
        RecoverProviderAccount = 1
    }

    public enum PlayServAuthOperationStatus
    {
        Success = 0,
        Conflict = 1,
        Failed = 2
    }

    public enum PlayServAuthErrorCode
    {
        None = 0,
        InvalidConfiguration = 1,
        InvalidCredential = 2,
        Unauthorized = 3,
        Forbidden = 4,
        DeviceBanned = 5,
        ProviderAlreadyLinked = 6,
        Network = 7,
        Timeout = 8,
        InvalidResponse = 9,
        PersistenceFailed = 10,
        RemoteSignOutFailed = 11,
        TransportReconnectFailed = 12,
        ServerError = 13,
        Unknown = 14,
        ProviderNotLinked = 15,
        LastProviderUnlinkForbidden = 16,
        MergeProviderConflict = 17,
        MergeOutcomeUnknown = 18,
        SessionRefreshFailed = 19,
        BrowserPopupBlocked = 20,
        BrowserFlowInProgress = 21,
        BrowserPlayerSwitchRequired = 22,
        BrowserClaimOutcomeUnknown = 23
    }

    public enum PlayServMergeChoice
    {
        KeepCurrentPlayer = 0,
        UseConflictingPlayer = 1
    }

    public enum PlayServAuthConflictKind
    {
        Unknown = 0,
        ProviderAlreadyLinked = 1,
        MergeProviderConflict = 2
    }

    public enum PlayServIdentityMutationState
    {
        None = 0,
        NotApplied = 1,
        Applied = 2,
        Unknown = 3
    }

    public enum PlayServSessionLostReason
    {
        Unknown = 0,
        RefreshRejected = 1,
        SessionRevoked = 2,
        SessionMerged = 3,
        Banned = 4,
        EnvironmentMismatch = 5,
        ExpiredWithoutRefresh = 6,
        CredentialRevoked = 7
    }

    public sealed class PlayServSessionInfo
    {
        internal PlayServSessionInfo(
            string playerId,
            PlayServSessionKind kind,
            IEnumerable<string> linkedProviders = null,
            bool areLinkedProvidersKnown = false)
        {
            PlayerId = playerId ?? string.Empty;
            Kind = kind;
            LinkedProviders = (linkedProviders ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            AreLinkedProvidersKnown = areLinkedProvidersKnown;
        }

        public string PlayerId { get; }

        public PlayServSessionKind Kind { get; }

        public IReadOnlyList<string> LinkedProviders { get; }

        public bool AreLinkedProvidersKnown { get; }

        public bool IsLoggedIn => Kind != PlayServSessionKind.None && !string.IsNullOrWhiteSpace(PlayerId);
    }

    public sealed class PlayServAuthError
    {
        internal PlayServAuthError(
            PlayServAuthErrorCode code,
            string message,
            int httpStatus,
            string backendCode,
            Exception exception,
            string backendTitle = null,
            string backendDetail = null)
        {
            Code = code;
            Message = message ?? string.Empty;
            HttpStatus = httpStatus;
            BackendCode = backendCode ?? string.Empty;
            Exception = exception;
            BackendTitle = backendTitle ?? string.Empty;
            BackendDetail = backendDetail ?? string.Empty;
            UnifiedError = CreateUnifiedError(
                code,
                Message,
                httpStatus,
                BackendCode,
                exception,
                BackendDetail);
        }

        public PlayServAuthErrorCode Code { get; }

        public string Message { get; }

        public int HttpStatus { get; }

        public string BackendCode { get; }

        public Exception Exception { get; }

        public string BackendTitle { get; }

        public string BackendDetail { get; }

        /// <summary>Cross-module representation of this authentication failure.</summary>
        public PlayServError UnifiedError { get; }

        public override string ToString()
        {
            var status = HttpStatus > 0 ? $" HTTP {HttpStatus}" : string.Empty;
            var backend = string.IsNullOrWhiteSpace(BackendCode) ? string.Empty : $" ({BackendCode})";
            return $"{Code}{status}{backend}: {Message}";
        }

        private static PlayServError CreateUnifiedError(
            PlayServAuthErrorCode code,
            string message,
            int httpStatus,
            string backendCode,
            Exception exception,
            string backendDetail)
        {
            var source = string.IsNullOrWhiteSpace(backendCode) ? code.ToString() : backendCode;
            var http = exception as PlayServRuntimeHttpException;
            var rawDetails = http?.ResponseBody;
            if (string.IsNullOrWhiteSpace(rawDetails))
                rawDetails = backendDetail;

            var commonCode = code switch
            {
                PlayServAuthErrorCode.None => PlayServErrorCode.None,
                PlayServAuthErrorCode.InvalidConfiguration => PlayServErrorCode.InvalidConfiguration,
                PlayServAuthErrorCode.InvalidCredential => PlayServErrorCode.Unauthorized,
                PlayServAuthErrorCode.Unauthorized => PlayServErrorCode.Unauthorized,
                PlayServAuthErrorCode.Forbidden => PlayServErrorCode.Forbidden,
                PlayServAuthErrorCode.DeviceBanned => PlayServErrorCode.Forbidden,
                PlayServAuthErrorCode.ProviderAlreadyLinked => PlayServErrorCode.Conflict,
                PlayServAuthErrorCode.ProviderNotLinked => PlayServErrorCode.NotFound,
                PlayServAuthErrorCode.LastProviderUnlinkForbidden => PlayServErrorCode.Validation,
                PlayServAuthErrorCode.MergeProviderConflict => PlayServErrorCode.Conflict,
                PlayServAuthErrorCode.MergeOutcomeUnknown => PlayServErrorCode.Unknown,
                PlayServAuthErrorCode.SessionRefreshFailed => PlayServErrorCode.Transport,
                PlayServAuthErrorCode.BrowserPopupBlocked => PlayServErrorCode.InvalidConfiguration,
                PlayServAuthErrorCode.BrowserFlowInProgress => PlayServErrorCode.Conflict,
                PlayServAuthErrorCode.BrowserPlayerSwitchRequired => PlayServErrorCode.Conflict,
                PlayServAuthErrorCode.BrowserClaimOutcomeUnknown => PlayServErrorCode.Unknown,
                PlayServAuthErrorCode.Network => PlayServErrorCode.Network,
                PlayServAuthErrorCode.Timeout => PlayServErrorCode.Timeout,
                PlayServAuthErrorCode.InvalidResponse => PlayServErrorCode.InvalidResponse,
                PlayServAuthErrorCode.PersistenceFailed => PlayServErrorCode.Persistence,
                PlayServAuthErrorCode.RemoteSignOutFailed => PlayServErrorCode.ServerError,
                PlayServAuthErrorCode.TransportReconnectFailed => PlayServErrorCode.Transport,
                PlayServAuthErrorCode.ServerError => PlayServErrorCode.ServerError,
                _ => PlayServErrorCode.Unknown
            };
            if (httpStatus == 408)
                commonCode = PlayServErrorCode.Timeout;
            else if (httpStatus == 404)
                commonCode = PlayServErrorCode.NotFound;
            else if (httpStatus == 409 || httpStatus == 412)
                commonCode = PlayServErrorCode.Conflict;
            else if (httpStatus == 422)
                commonCode = PlayServErrorCode.Validation;
            else if (httpStatus == 425 || httpStatus == 429)
                commonCode = PlayServErrorCode.RateLimited;
            else if (httpStatus >= 500)
                commonCode = PlayServErrorCode.ServerError;
            var retryable = code == PlayServAuthErrorCode.Network ||
                            code == PlayServAuthErrorCode.Timeout ||
                            code == PlayServAuthErrorCode.ServerError ||
                            code == PlayServAuthErrorCode.RemoteSignOutFailed ||
                            code == PlayServAuthErrorCode.TransportReconnectFailed ||
                            code == PlayServAuthErrorCode.MergeOutcomeUnknown ||
                            code == PlayServAuthErrorCode.SessionRefreshFailed ||
                            httpStatus == 408 || httpStatus == 425 || httpStatus == 429 || httpStatus >= 500;
            return new PlayServError(
                commonCode,
                source,
                message,
                httpStatus > 0 ? (int?)httpStatus : null,
                null,
                retryable,
                rawDetails);
        }
    }

    public sealed class PlayServAuthConflictParty
    {
        internal PlayServAuthConflictParty(
            string playerId,
            PlayServSessionKind kind,
            DateTimeOffset joinedAt,
            DateTimeOffset? lastSeenAt)
        {
            PlayerId = playerId ?? string.Empty;
            Kind = kind;
            JoinedAt = joinedAt;
            LastSeenAt = lastSeenAt;
        }

        public string PlayerId { get; }

        public PlayServSessionKind Kind { get; }

        public DateTimeOffset JoinedAt { get; }

        public DateTimeOffset? LastSeenAt { get; }
    }

    public sealed class PlayServAuthConflict
    {
        internal PlayServAuthConflict(
            string providerId,
            PlayServAuthConflictParty current,
            PlayServAuthConflictParty conflicting,
            PlayServAuthConflictKind kind = PlayServAuthConflictKind.ProviderAlreadyLinked,
            IEnumerable<string> conflictingProviders = null)
        {
            ProviderId = providerId ?? string.Empty;
            Current = current;
            Conflicting = conflicting;
            Kind = kind;
            ConflictingProviders = (conflictingProviders ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
        }

        public string ProviderId { get; }

        public PlayServAuthConflictParty Current { get; }

        public PlayServAuthConflictParty Conflicting { get; }

        public PlayServAuthConflictKind Kind { get; }

        public IReadOnlyList<string> ConflictingProviders { get; }
    }

    public sealed class PlayServAuthResult
    {
        internal PlayServAuthResult(
            PlayServAuthOperationStatus status,
            PlayServSessionInfo session,
            PlayServAuthError error,
            PlayServAuthConflict conflict,
            bool transportReady,
            PlayServIdentityMutationState identityMutationState = PlayServIdentityMutationState.None)
        {
            Status = status;
            Session = session;
            Error = error;
            Conflict = conflict;
            TransportReady = transportReady;
            IdentityMutationState = identityMutationState;
        }

        public PlayServAuthOperationStatus Status { get; }

        public bool IsSuccess => Status == PlayServAuthOperationStatus.Success;

        public PlayServSessionInfo Session { get; }

        public PlayServAuthError Error { get; }

        /// <summary>Cross-module failure, or <c>null</c> when the operation succeeded.</summary>
        public PlayServError UnifiedError => Error?.UnifiedError;

        public PlayServAuthConflict Conflict { get; }

        public bool TransportReady { get; }

        public PlayServIdentityMutationState IdentityMutationState { get; }
    }

    public sealed class PlayServAuthProviderInfo
    {
        internal PlayServAuthProviderInfo(
            string id,
            string label,
            bool enabled,
            string connectivity,
            bool available,
            bool supportsProviderToken)
        {
            Id = id ?? string.Empty;
            Label = label ?? string.Empty;
            Enabled = enabled;
            Connectivity = connectivity ?? string.Empty;
            Available = available;
            SupportsProviderToken = supportsProviderToken;
        }

        public string Id { get; }
        public string Label { get; }
        public bool Enabled { get; }
        public string Connectivity { get; }
        public bool Available { get; }
        public bool SupportsProviderToken { get; }
    }

    public sealed class PlayServAuthProvidersResult
    {
        internal PlayServAuthProvidersResult(
            string projectId,
            string projectSlug,
            string environment,
            IEnumerable<PlayServAuthProviderInfo> providers,
            PlayServAuthError error)
        {
            ProjectId = projectId ?? string.Empty;
            ProjectSlug = projectSlug ?? string.Empty;
            Environment = environment ?? string.Empty;
            Providers = (providers ?? Array.Empty<PlayServAuthProviderInfo>()).ToArray();
            Error = error;
        }

        public bool IsSuccess => Error == null;
        public string ProjectId { get; }
        public string ProjectSlug { get; }
        public string Environment { get; }
        public IReadOnlyList<PlayServAuthProviderInfo> Providers { get; }
        public PlayServAuthError Error { get; }
        public PlayServError UnifiedError => Error?.UnifiedError;
    }

    /// <summary>
    /// Safe runtime projection of the currently authenticated player. Operator-only
    /// moderation, IP, fingerprint and merge-forensics fields are never exposed here.
    /// </summary>
    public sealed class PlayServPlayerProfile
    {
        internal PlayServPlayerProfile(
            string id,
            string kind,
            string name,
            string email,
            string status,
            IEnumerable<string> linkedProviders,
            string country,
            DateTimeOffset? lastSeenAt,
            string joinedDate,
            DateTimeOffset createdAt,
            DateTimeOffset updatedAt)
        {
            Id = id ?? string.Empty;
            Kind = kind ?? string.Empty;
            Name = name ?? string.Empty;
            Email = email;
            Status = status ?? string.Empty;
            LinkedProviders = (linkedProviders ?? Array.Empty<string>()).ToArray();
            Country = country;
            LastSeenAt = lastSeenAt;
            JoinedDate = joinedDate ?? string.Empty;
            CreatedAt = createdAt;
            UpdatedAt = updatedAt;
        }

        public string Id { get; }
        public string Kind { get; }
        public string Name { get; }
        public string Email { get; }
        public string Status { get; }
        public IReadOnlyList<string> LinkedProviders { get; }
        public string Country { get; }
        public DateTimeOffset? LastSeenAt { get; }

        /// <summary>Backend ISO-8601 calendar date (<c>yyyy-MM-dd</c>).</summary>
        public string JoinedDate { get; }

        public DateTimeOffset CreatedAt { get; }
        public DateTimeOffset UpdatedAt { get; }
    }

    public sealed class PlayServPlayerProfileResult
    {
        internal PlayServPlayerProfileResult(
            PlayServPlayerProfile profile,
            string etag,
            bool isFromCache,
            PlayServAuthError error)
        {
            Profile = profile;
            ETag = etag ?? string.Empty;
            IsFromCache = isFromCache;
            Error = error;
        }

        public bool IsSuccess => Error == null && Profile != null;
        public PlayServPlayerProfile Profile { get; }
        public string ETag { get; }
        public bool IsFromCache { get; }
        public PlayServAuthError Error { get; }
        public PlayServError UnifiedError => Error?.UnifiedError;
    }

    [Serializable]
    internal sealed class PlayServPlayerProfileDto
    {
        public string id;
        public string kind;
        public string name;
        public string email;
        public string status;
        public string[] sso;
        public string country;
        public string last_seen;
        public string joined;
        public string created_at;
        public string updated_at;
    }

    public sealed class PlayServSessionLostInfo
    {
        internal PlayServSessionLostInfo(
            PlayServSessionLostReason reason,
            PlayServSessionInfo previousSession,
            string message,
            bool canRetry)
        {
            Reason = reason;
            PreviousSession = previousSession;
            Message = message ?? string.Empty;
            CanRetry = canRetry;
        }

        public PlayServSessionLostReason Reason { get; }

        public PlayServSessionInfo PreviousSession { get; }

        public string Message { get; }

        public bool CanRetry { get; }
    }
}
