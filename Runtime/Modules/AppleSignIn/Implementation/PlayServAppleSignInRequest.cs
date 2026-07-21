namespace Playserv.AppleSignIn
{
    public sealed class PlayServAppleSignInRequest
    {
        public PlayServAppleSignInRequest(
            PlayServAppleSignInScope scopes = PlayServAppleSignInScope.Email | PlayServAppleSignInScope.FullName,
            string nonce = null,
            string state = null)
        {
            Scopes = scopes;
            Nonce = nonce ?? string.Empty;
            State = state ?? string.Empty;
        }

        public PlayServAppleSignInScope Scopes { get; set; }

        public string Nonce { get; set; }

        public string State { get; set; }
    }
}
