using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Runtime.Abstractions;
using UnityEngine;

namespace Playserv.Wrapper
{
    internal sealed class PlayServPlayerPrefsSessionStore : IPlayServPlayerSessionStore
    {
        private const string KeyPrefix = "PlayServ.PlayerAuth.";

        public Task<PlayServPlayerSessionData> LoadAsync(
            string scopeKey,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var prefix = BuildKeyPrefix(scopeKey);
            var playerId = PlayerPrefs.GetString(prefix + "PlayerId", string.Empty);
            var refreshToken = PlayerPrefs.GetString(prefix + "RefreshToken", string.Empty);
            var kindValue = PlayerPrefs.GetInt(prefix + "SessionKind", (int)PlayServSessionKind.Anonymous);
            var refreshExpiryText = PlayerPrefs.GetString(prefix + "RefreshExpiresAtUtc", string.Empty);
            var kind = Enum.IsDefined(typeof(PlayServSessionKind), kindValue)
                ? (PlayServSessionKind)kindValue
                : PlayServSessionKind.Anonymous;
            DateTimeOffset? refreshExpiry = DateTimeOffset.TryParse(refreshExpiryText, out var parsedExpiry)
                ? (DateTimeOffset?)parsedExpiry
                : null;
            var session = string.IsNullOrWhiteSpace(playerId) || string.IsNullOrWhiteSpace(refreshToken)
                ? null
                : new PlayServPlayerSessionData(playerId, refreshToken, kind, refreshExpiry);
            return Task.FromResult(session);
        }

        public Task SaveAsync(
            string scopeKey,
            PlayServPlayerSessionData session,
            CancellationToken cancellationToken = default)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));

            cancellationToken.ThrowIfCancellationRequested();
            var prefix = BuildKeyPrefix(scopeKey);
            PlayerPrefs.SetString(prefix + "PlayerId", session.PlayerId ?? string.Empty);
            PlayerPrefs.SetString(prefix + "RefreshToken", session.RefreshToken ?? string.Empty);
            PlayerPrefs.SetInt(prefix + "SessionKind", (int)session.SessionKind);
            PlayerPrefs.SetString(
                prefix + "RefreshExpiresAtUtc",
                session.RefreshTokenExpiresAtUtc?.ToString("O") ?? string.Empty);
            PlayerPrefs.Save();
            return Task.CompletedTask;
        }

        public Task ClearAsync(
            string scopeKey,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var prefix = BuildKeyPrefix(scopeKey);
            PlayerPrefs.DeleteKey(prefix + "PlayerId");
            PlayerPrefs.DeleteKey(prefix + "RefreshToken");
            PlayerPrefs.DeleteKey(prefix + "SessionKind");
            PlayerPrefs.DeleteKey(prefix + "RefreshExpiresAtUtc");
            PlayerPrefs.Save();
            return Task.CompletedTask;
        }

        private static string BuildKeyPrefix(string scopeKey)
        {
            return KeyPrefix + SanitizeKey(scopeKey) + ".";
        }

        private static string SanitizeKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "default";

            var builder = new StringBuilder(value.Length);
            foreach (var character in value)
            {
                if (char.IsLetterOrDigit(character) || character == '-' || character == '_' || character == '.')
                    builder.Append(character);
                else
                    builder.Append('_');
            }

            return builder.ToString();
        }
    }
}
