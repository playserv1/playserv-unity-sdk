#if UNITY_5_3_OR_NEWER
using UnityEngine;

namespace Playserv.AppleSignIn
{
    [CreateAssetMenu(fileName = "PlayServAppleSignInSettings", menuName = "PlayServ/Apple Sign In Settings", order = 2)]
    public sealed class PlayServAppleSignInSettings : ScriptableObject
    {
        [SerializeField] private bool requestEmail = true;
        [SerializeField] private bool requestFullName = true;
        [SerializeField] private string defaultNonce = "";
        [SerializeField] private string defaultState = "";
        [SerializeField] private string clientId = "";
        [SerializeField] private bool addSignInCapabilityOnBuild = true;
        [SerializeField] private string entitlementsFileName = "PlayServAppleSignIn.entitlements";

        public bool RequestEmail => requestEmail;

        public bool RequestFullName => requestFullName;

        public string DefaultNonce => defaultNonce;

        public string DefaultState => defaultState;

        public string ClientId => clientId;

        public bool AddSignInCapabilityOnBuild => addSignInCapabilityOnBuild;

        public string EntitlementsFileName => string.IsNullOrWhiteSpace(entitlementsFileName)
            ? "PlayServAppleSignIn.entitlements"
            : entitlementsFileName.Trim();

        public PlayServAppleSignInRequest CreateDefaultRequest()
        {
            var scopes = PlayServAppleSignInScope.None;
            if (requestEmail)
                scopes |= PlayServAppleSignInScope.Email;
            if (requestFullName)
                scopes |= PlayServAppleSignInScope.FullName;

            return new PlayServAppleSignInRequest(scopes, defaultNonce, defaultState);
        }
    }
}
#endif
