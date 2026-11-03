using System;

namespace Playserv.GoogleSignIn
{
    public sealed class PlayServGoogleSignInRequest
    {
        public PlayServGoogleSignInRequest(
            string webClientId = null,
            bool? requestIdToken = null,
            bool? requestAuthCode = null,
            bool? requestEmail = null,
            bool? forceTokenRefresh = null,
            bool? useGameSignIn = null,
            string hostedDomain = null,
            string accountName = null,
            string[] additionalScopes = null)
        {
            WebClientId = webClientId;
            RequestIdToken = requestIdToken;
            RequestAuthCode = requestAuthCode;
            RequestEmail = requestEmail;
            ForceTokenRefresh = forceTokenRefresh;
            UseGameSignIn = useGameSignIn;
            HostedDomain = hostedDomain;
            AccountName = accountName;
            AdditionalScopes = additionalScopes ?? Array.Empty<string>();
        }

        public string WebClientId { get; set; }

        public bool? RequestIdToken { get; set; }

        public bool? RequestAuthCode { get; set; }

        public bool? RequestEmail { get; set; }

        public bool? ForceTokenRefresh { get; set; }

        public bool? UseGameSignIn { get; set; }

        public string HostedDomain { get; set; }

        public string AccountName { get; set; }

        public string[] AdditionalScopes { get; set; }
    }
}
