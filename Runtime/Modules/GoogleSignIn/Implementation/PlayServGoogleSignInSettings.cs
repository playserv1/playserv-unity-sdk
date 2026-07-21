#if UNITY_5_3_OR_NEWER
using System;
using UnityEngine;

namespace Playserv.GoogleSignIn
{
    [CreateAssetMenu(fileName = "PlayServGoogleSignInSettings", menuName = "PlayServ/Google Sign In Settings", order = 3)]
    public sealed class PlayServGoogleSignInSettings : ScriptableObject
    {
        [SerializeField] private string webClientId = "";
        [SerializeField] private bool requestIdToken = true;
        [SerializeField] private bool requestAuthCode = true;
        [SerializeField] private bool requestEmail = true;
        [SerializeField] private bool forceTokenRefresh;
        [SerializeField] private bool useGameSignIn;
        [SerializeField] private string hostedDomain = "";
        [SerializeField] private string accountName = "";
        [SerializeField] private string[] additionalScopes = Array.Empty<string>();

        public string WebClientId => webClientId;

        public bool RequestIdToken => requestIdToken;

        public bool RequestAuthCode => requestAuthCode;

        public bool RequestEmail => requestEmail;

        public bool ForceTokenRefresh => forceTokenRefresh;

        public bool UseGameSignIn => useGameSignIn;

        public string HostedDomain => hostedDomain;

        public string AccountName => accountName;

        public string[] AdditionalScopes => additionalScopes ?? Array.Empty<string>();

        public PlayServGoogleSignInRequest CreateDefaultRequest()
        {
            return new PlayServGoogleSignInRequest(
                webClientId: webClientId,
                requestIdToken: requestIdToken,
                requestAuthCode: requestAuthCode,
                requestEmail: requestEmail,
                forceTokenRefresh: forceTokenRefresh,
                useGameSignIn: useGameSignIn,
                hostedDomain: hostedDomain,
                accountName: accountName,
                additionalScopes: AdditionalScopes);
        }
    }
}
#endif
