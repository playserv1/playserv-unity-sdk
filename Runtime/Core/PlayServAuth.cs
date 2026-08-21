using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Identity;

namespace Playserv.Wrapper
{
    public static class PlayServAuth
    {
        private static IPlayServAuthApi Api => PlayServApiHost.Auth;

        public static bool IsLoggedIn => Api.IsLoggedIn;

        public static string PlayerId => Api.PlayerId;

        public static PlayServSessionKind SessionKind => Api.SessionKind;

        public static PlayServSessionInfo CurrentSession => Api.CurrentSession;

        public static IReadOnlyList<string> LinkedProviders => CurrentSession.LinkedProviders;

        public static bool AreLinkedProvidersKnown => CurrentSession.AreLinkedProvidersKnown;

        public static event Action<PlayServSessionLostInfo> SessionLost
        {
            add => Api.SessionLost += value;
            remove => Api.SessionLost -= value;
        }

        public static Task<PlayServAuthResult> LoginExternalAsync(
            PlayServExternalIdentityProof proof,
            PlayServExternalLoginMode mode = PlayServExternalLoginMode.PreserveCurrentPlayer,
            CancellationToken cancellationToken = default) =>
            Api.LoginExternalAsync(proof, mode, cancellationToken);

        public static Task<PlayServAuthProvidersResult> GetProvidersAsync(
            CancellationToken cancellationToken = default) =>
            Api.GetProvidersAsync(cancellationToken);

        /// <summary>Links a freshly verified provider credential to the managed player.</summary>
        public static Task<PlayServAuthResult> LinkIdentityAsync(
            PlayServExternalIdentityProof proof,
            CancellationToken cancellationToken = default) =>
            Api.LinkIdentityAsync(proof, cancellationToken);

        /// <summary>Unlinks a provider and refreshes live authorization claims.</summary>
        public static Task<PlayServAuthResult> UnlinkIdentityAsync(
            string providerId,
            CancellationToken cancellationToken = default) =>
            Api.UnlinkIdentityAsync(providerId, cancellationToken);

        /// <summary>Merges the players described by a typed provider conflict.</summary>
        public static Task<PlayServAuthResult> MergeIdentityAsync(
            PlayServAuthConflict conflict,
            PlayServMergeChoice choice,
            PlayServExternalIdentityProof proof,
            CancellationToken cancellationToken = default) =>
            Api.MergeIdentityAsync(conflict, choice, proof, cancellationToken);

        public static Task<PlayServAuthResult> LogoutAsync(
            CancellationToken cancellationToken = default) =>
            Api.LogoutAsync(cancellationToken);
    }
}
