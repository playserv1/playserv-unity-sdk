using Playserv.Identity;

namespace Playserv.AppleSignIn
{
    /// <summary>
    /// Credential returned by the Apple provider. User and profile fields are
    /// untrusted until a backend validates the identity token.
    /// </summary>
    public sealed class PlayServAppleSignInCredential
    {
        internal PlayServAppleSignInCredential(
            PlayServAppleCredentialType credentialType,
            string userId,
            string email,
            string fullName,
            string givenName,
            string familyName,
            string identityToken,
            string authorizationCode,
            string state,
            string password)
        {
            CredentialType = credentialType;
            UserId = userId ?? string.Empty;
            Email = email ?? string.Empty;
            FullName = fullName ?? string.Empty;
            GivenName = givenName ?? string.Empty;
            FamilyName = familyName ?? string.Empty;
            IdentityToken = identityToken ?? string.Empty;
            AuthorizationCode = authorizationCode ?? string.Empty;
            State = state ?? string.Empty;
            Password = password ?? string.Empty;
        }

        public PlayServAppleCredentialType CredentialType { get; }

        public string UserId { get; }

        public string Email { get; }

        public string FullName { get; }

        public string GivenName { get; }

        public string FamilyName { get; }

        public string IdentityToken { get; }

        public string AuthorizationCode { get; }

        public string State { get; }

        public string Password { get; }

        public bool IsAppleIdCredential => CredentialType == PlayServAppleCredentialType.AppleId;

        public bool IsPasswordCredential => CredentialType == PlayServAppleCredentialType.Password;

        public bool TryCreateBackendProof(out PlayServExternalIdentityProof proof)
        {
            if (string.IsNullOrWhiteSpace(IdentityToken))
            {
                proof = null;
                return false;
            }

            proof = PlayServExternalIdentityProof.FromProviderToken(
                PlayServIdentityProviderIds.Apple,
                IdentityToken);
            return true;
        }
    }
}
