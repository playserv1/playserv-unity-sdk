using System;

namespace Playserv.Http.Interfaces
{
    /// <summary>
    /// Wire response returned by <c>POST /auth/players/anon</c>.
    /// Field names intentionally match the backend JSON contract.
    /// </summary>
    [Serializable]
    public sealed class PlayerTokenBundleDto
    {
        public string player_id;
        public string access_token;
        public string refresh_token;
        public int expires_in;
        public string issued_at;
    }
}
