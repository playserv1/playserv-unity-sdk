namespace Playserv.Runtime.Abstractions
{
    /// <summary>
    /// Runtime-only data required to restore a PlayServ player session.
    /// </summary>
    public sealed class PlayServPlayerSessionData
    {
        public PlayServPlayerSessionData(string playerId, string refreshToken)
        {
            PlayerId = playerId ?? string.Empty;
            RefreshToken = refreshToken ?? string.Empty;
        }

        public string PlayerId { get; }

        public string RefreshToken { get; }
    }
}
