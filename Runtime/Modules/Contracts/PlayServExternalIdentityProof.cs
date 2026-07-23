using System;

namespace Playserv.Identity
{
    public static class PlayServIdentityProviderIds
    {
        public const string Apple = "apple";
        public const string Google = "google";
    }

    /// <summary>
    /// Unverified provider credential intended for a future PlayServ backend
    /// authentication endpoint. It must never be treated as a PlayServ session
    /// until the backend validates the provider token or authorization code.
    /// </summary>
    public sealed class PlayServExternalIdentityProof
    {
        public PlayServExternalIdentityProof(
            string providerId,
            string idToken,
            string authorizationCode)
        {
            if (string.IsNullOrWhiteSpace(providerId))
                throw new ArgumentException("Identity provider id is required.", nameof(providerId));

            ProviderId = providerId.Trim();
            IdToken = idToken ?? string.Empty;
            AuthorizationCode = authorizationCode ?? string.Empty;

            if (!HasIdToken && !HasAuthorizationCode)
            {
                throw new ArgumentException(
                    "An identity token or authorization code is required.",
                    nameof(idToken));
            }
        }

        public string ProviderId { get; }

        public string IdToken { get; }

        public string AuthorizationCode { get; }

        public bool HasIdToken => !string.IsNullOrWhiteSpace(IdToken);

        public bool HasAuthorizationCode => !string.IsNullOrWhiteSpace(AuthorizationCode);
    }
}
