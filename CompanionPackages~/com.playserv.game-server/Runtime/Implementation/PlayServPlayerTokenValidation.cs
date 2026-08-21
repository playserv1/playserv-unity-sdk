using System;
using System.Collections.Generic;
using Playserv.Wrapper;

namespace Playserv.GameServer
{
    /// <summary>Expected scope and lifetime policy for offline player-token validation.</summary>
    public sealed class PlayServPlayerTokenValidationOptions
    {
        /// <summary>Required expected <c>project_id</c> claim.</summary>
        public string ExpectedProjectId { get; set; }

        /// <summary>Required expected <c>env</c> claim.</summary>
        public string ExpectedEnvironment { get; set; }

        /// <summary>Expected issuer. Defaults to <c>playserv</c>.</summary>
        public string Issuer { get; set; } = "playserv";

        /// <summary>Allowed lifetime clock skew. Defaults to 60 seconds.</summary>
        public TimeSpan ClockSkew { get; set; } = TimeSpan.FromSeconds(60);
    }

    /// <summary>Safe, verified claims from a PlayServ runtime player token.</summary>
    public sealed class PlayServValidatedPlayerClaims
    {
        internal PlayServValidatedPlayerClaims(
            string playerId,
            string projectId,
            string environment,
            string sessionId,
            IReadOnlyList<string> providers,
            DateTimeOffset? issuedAt,
            DateTimeOffset? notBefore,
            DateTimeOffset expiresAt,
            string tokenId)
        {
            PlayerId = playerId;
            ProjectId = projectId;
            Environment = environment;
            SessionId = sessionId;
            Providers = providers ?? Array.Empty<string>();
            IssuedAt = issuedAt;
            NotBefore = notBefore;
            ExpiresAt = expiresAt;
            TokenId = tokenId;
        }

        public string PlayerId { get; }
        public string ProjectId { get; }
        public string Environment { get; }
        public string SessionId { get; }
        public IReadOnlyList<string> Providers { get; }
        public DateTimeOffset? IssuedAt { get; }
        public DateTimeOffset? NotBefore { get; }
        public DateTimeOffset ExpiresAt { get; }
        public string TokenId { get; }
    }

    /// <summary>Non-throwing validation outcome. Argument misuse and cancellation still throw.</summary>
    public sealed class PlayServPlayerTokenValidationResult
    {
        private PlayServPlayerTokenValidationResult(
            PlayServValidatedPlayerClaims claims,
            PlayServError error)
        {
            Claims = claims;
            UnifiedError = error ?? PlayServError.None;
        }

        public bool IsValid => Claims != null && !UnifiedError.IsError;
        public PlayServValidatedPlayerClaims Claims { get; }
        public PlayServError UnifiedError { get; }

        internal static PlayServPlayerTokenValidationResult Valid(
            PlayServValidatedPlayerClaims claims) =>
            new PlayServPlayerTokenValidationResult(claims, PlayServError.None);

        internal static PlayServPlayerTokenValidationResult Invalid(PlayServError error) =>
            new PlayServPlayerTokenValidationResult(null, error);
    }
}
