using Playserv.Identity;

namespace Playserv.EpicAuth
{
    /// <summary>Unverified Epic credential copied from EOS or supplied by the Epic Games Launcher.</summary>
    public sealed class PlayServEpicCredential
    {
        internal PlayServEpicCredential(PlayServEpicCredentialSource source, string token)
        {
            Source = source;
            Token = token ?? string.Empty;
        }

        public PlayServEpicCredentialSource Source { get; }

        public string Token { get; }

        public bool TryCreateBackendProof(out PlayServExternalIdentityProof proof)
        {
            if (string.IsNullOrWhiteSpace(Token))
            {
                proof = null;
                return false;
            }

            proof = PlayServExternalIdentityProof.FromEpicExternalAuthToken(
                Token,
                Source == PlayServEpicCredentialSource.LauncherExchangeCode);
            return true;
        }
    }
}
