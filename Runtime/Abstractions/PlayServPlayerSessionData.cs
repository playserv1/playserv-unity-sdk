using System;
using Playserv.Wrapper;

namespace Playserv.Runtime.Abstractions
{
    /// <summary>
    /// Runtime-only data required to restore a PlayServ player session.
    /// </summary>
    public sealed class PlayServPlayerSessionData
    {
        public PlayServPlayerSessionData(string playerId, string refreshToken)
            : this(playerId, refreshToken, PlayServSessionKind.Anonymous, null)
        {
        }

        public PlayServPlayerSessionData(
            string playerId,
            string refreshToken,
            PlayServSessionKind sessionKind,
            DateTimeOffset? refreshTokenExpiresAtUtc)
        {
            PlayerId = playerId ?? string.Empty;
            RefreshToken = refreshToken ?? string.Empty;
            SessionKind = sessionKind;
            RefreshTokenExpiresAtUtc = refreshTokenExpiresAtUtc;
        }

        public string PlayerId { get; }

        public string RefreshToken { get; }

        public PlayServSessionKind SessionKind { get; }

        public DateTimeOffset? RefreshTokenExpiresAtUtc { get; }
    }
}
