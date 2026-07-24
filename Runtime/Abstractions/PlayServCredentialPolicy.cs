using System;

namespace Playserv.Runtime.Abstractions
{
    internal static class PlayServCredentialPolicy
    {
        private const string BearerPrefix = "Bearer ";
        private const string PublicKeyPrefix = "pk_";
        private const string SecretKeyPrefix = "sk_";

        public static string NormalizeClientToken(string value)
        {
            var token = NormalizeCredential(value);
            if (token == null)
                return null;

            if (!token.StartsWith(PublicKeyPrefix, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "PlayServ ClientToken must be a public pk_* key. " +
                    "Player JWTs must be supplied through IPlayServRuntimeTokenProvider.");
            }

            return token;
        }

        public static string NormalizePlayerAuthorization(string value)
        {
            var token = NormalizeCredential(value);
            if (token == null)
                return null;

            if (token.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
                token = NormalizeCredential(token.Substring(BearerPrefix.Length));

            if (token == null)
                return null;

            if (token.StartsWith(SecretKeyPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "PlayServ secret sk_* keys are forbidden in the player runtime. " +
                    "Use a player JWT from IPlayServRuntimeTokenProvider.");
            }

            if (token.StartsWith(PublicKeyPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "IPlayServRuntimeTokenProvider must return a player JWT, not a public pk_* client token.");
            }

            return BearerPrefix + token;
        }

        public static string ExtractBearerToken(string authorization)
        {
            var normalized = NormalizePlayerAuthorization(authorization);
            return normalized == null
                ? null
                : normalized.Substring(BearerPrefix.Length);
        }

        private static string NormalizeCredential(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var normalized = value.Trim();
            if (normalized.IndexOf('\r') >= 0 || normalized.IndexOf('\n') >= 0)
                throw new InvalidOperationException("PlayServ credentials cannot contain line breaks.");

            return normalized;
        }
    }
}
