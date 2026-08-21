using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Identity;
using Playserv.Wrapper;

namespace Playserv.Samples.Identity
{
    /// <summary>
    /// Identity-lifecycle helpers intended to be called after the platform provider SDK
    /// has returned a fresh credential. UI and consent remain game responsibilities.
    /// </summary>
    public static class PlayServIdentityLifecycleSample
    {
        public static void EnableAutomaticMobileFingerprint(PlayServSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            settings.EnableAutomaticPlayerFingerprint = true;
        }

        public static Task<PlayServAuthProvidersResult> DiscoverProvidersAsync(
            CancellationToken cancellationToken = default) =>
            PlayServAuth.GetProvidersAsync(cancellationToken);

        public static async Task<PlayServAuthResult> LinkOrMergeGoogleAsync(
            string googleIdToken,
            string nonce,
            PlayServMergeChoice mergeChoice,
            Func<CancellationToken, Task<string>> acquireFreshGoogleIdToken,
            CancellationToken cancellationToken = default)
        {
            var proof = PlayServExternalIdentityProof.FromGoogleIdToken(googleIdToken, nonce);
            var linked = await PlayServAuth.LinkIdentityAsync(proof, cancellationToken);
            if (linked.Conflict?.Current == null || linked.Conflict.Conflicting == null)
                return linked;

            if (acquireFreshGoogleIdToken == null)
                throw new ArgumentNullException(nameof(acquireFreshGoogleIdToken));

            var freshToken = await acquireFreshGoogleIdToken(cancellationToken);
            var freshProof = PlayServExternalIdentityProof.FromGoogleIdToken(freshToken, nonce);
            return await PlayServAuth.MergeIdentityAsync(
                linked.Conflict,
                mergeChoice,
                freshProof,
                cancellationToken);
        }

        public static Task<PlayServAuthResult> UnlinkGoogleAsync(
            CancellationToken cancellationToken = default) =>
            PlayServAuth.UnlinkIdentityAsync(
                PlayServIdentityProviderIds.Google,
                cancellationToken);

        // The Steam, Epic, and Facebook companion samples acquire these values
        // from their provider SDKs. These helpers show the equivalent core proof
        // shape for games that already own a provider integration.
        public static Task<PlayServAuthResult> LoginSteamTicketAsync(
            string webApiTicket,
            CancellationToken cancellationToken = default) =>
            PlayServAuth.LoginExternalAsync(
                PlayServExternalIdentityProof.FromSteamTicket(webApiTicket),
                PlayServExternalLoginMode.PreserveCurrentPlayer,
                cancellationToken);

        public static Task<PlayServAuthResult> LoginEpicAccessTokenAsync(
            string accessToken,
            CancellationToken cancellationToken = default) =>
            PlayServAuth.LoginExternalAsync(
                PlayServExternalIdentityProof.FromEpicExternalAuthToken(accessToken),
                PlayServExternalLoginMode.PreserveCurrentPlayer,
                cancellationToken);

        public static Task<PlayServAuthResult> LoginFacebookLimitedAsync(
            string authenticationToken,
            string nonce,
            CancellationToken cancellationToken = default) =>
            PlayServAuth.LoginExternalAsync(
                PlayServExternalIdentityProof.FromFacebookLimitedLoginToken(
                    authenticationToken,
                    nonce),
                PlayServExternalLoginMode.PreserveCurrentPlayer,
                cancellationToken);

        public static PlayServPlayerFingerprint CreateConsentedDesktopSignals(
            string appVersion,
            string locale) =>
            new PlayServPlayerFingerprint(
                new Dictionary<string, object> { ["platform"] = "windows" },
                new Dictionary<string, object>
                {
                    ["app_version"] = appVersion,
                    ["locale"] = locale
                });
    }
}
