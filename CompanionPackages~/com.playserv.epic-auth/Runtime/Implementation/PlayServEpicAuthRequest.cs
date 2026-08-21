namespace Playserv.EpicAuth
{
    /// <summary>The Epic credential path that PlayServ must validate.</summary>
    public enum PlayServEpicCredentialSource
    {
        EosAccessToken = 0,
        LauncherExchangeCode = 1
    }

    /// <summary>Selects an EAS access token or Epic Games Launcher exchange code.</summary>
    public sealed class PlayServEpicAuthRequest
    {
        private PlayServEpicAuthRequest(
            PlayServEpicCredentialSource source,
            string epicAccountId,
            string exchangeCode)
        {
            Source = source;
            EpicAccountId = epicAccountId?.Trim() ?? string.Empty;
            ExchangeCode = exchangeCode?.Trim() ?? string.Empty;
        }

        public PlayServEpicCredentialSource Source { get; }

        public string EpicAccountId { get; }

        public string ExchangeCode { get; }

        /// <summary>Uses EOS Auth, optionally selecting an explicit Epic Account ID.</summary>
        public static PlayServEpicAuthRequest Eos(string epicAccountId = null) =>
            new PlayServEpicAuthRequest(PlayServEpicCredentialSource.EosAccessToken, epicAccountId, null);

        /// <summary>Uses an explicit or launcher-supplied exchange code.</summary>
        public static PlayServEpicAuthRequest Launcher(string exchangeCode = null) =>
            new PlayServEpicAuthRequest(PlayServEpicCredentialSource.LauncherExchangeCode, null, exchangeCode);
    }
}
