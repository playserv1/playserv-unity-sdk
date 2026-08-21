using Playserv.Identity;

namespace Playserv.FacebookLogin
{
    /// <summary>Unverified Facebook Limited Login authentication token and its bound nonce.</summary>
    public sealed class PlayServFacebookCredential
    {
        internal PlayServFacebookCredential(string authenticationToken, string nonce)
        {
            AuthenticationToken = authenticationToken ?? string.Empty;
            Nonce = nonce ?? string.Empty;
        }

        public string AuthenticationToken { get; }

        public string Nonce { get; }

        public bool TryCreateBackendProof(out PlayServExternalIdentityProof proof)
        {
            if (string.IsNullOrWhiteSpace(AuthenticationToken) || string.IsNullOrWhiteSpace(Nonce))
            {
                proof = null;
                return false;
            }

            proof = PlayServExternalIdentityProof.FromFacebookLimitedLoginToken(
                AuthenticationToken,
                Nonce);
            return true;
        }
    }
}
