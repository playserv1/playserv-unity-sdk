using Playserv.Identity;

namespace Playserv.GoogleSignIn
{
    /// <summary>
    /// Credential returned by the Google provider. User and profile fields are
    /// untrusted until a backend validates the ID token or authorization code.
    /// </summary>
    public sealed class PlayServGoogleSignInCredential
    {
        internal PlayServGoogleSignInCredential(
            string userId,
            string email,
            string displayName,
            string givenName,
            string familyName,
            string photoUrl,
            string idToken,
            string authCode)
        {
            UserId = userId;
            Email = email;
            DisplayName = displayName;
            GivenName = givenName;
            FamilyName = familyName;
            PhotoUrl = photoUrl;
            IdToken = idToken;
            AuthCode = authCode;
        }

        public string UserId { get; }

        public string Email { get; }

        public string DisplayName { get; }

        public string GivenName { get; }

        public string FamilyName { get; }

        public string PhotoUrl { get; }

        public string IdToken { get; }

        public string AuthCode { get; }

        public string ServerAuthCode => AuthCode;

        public bool TryCreateBackendProof(out PlayServExternalIdentityProof proof)
        {
            if (string.IsNullOrWhiteSpace(IdToken) &&
                string.IsNullOrWhiteSpace(AuthCode))
            {
                proof = null;
                return false;
            }

            proof = new PlayServExternalIdentityProof(
                PlayServIdentityProviderIds.Google,
                IdToken,
                AuthCode);
            return true;
        }
    }
}
