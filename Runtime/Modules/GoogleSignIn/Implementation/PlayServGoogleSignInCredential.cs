namespace Playserv.GoogleSignIn
{
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
    }
}
