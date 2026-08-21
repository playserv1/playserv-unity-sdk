using System;

namespace Playserv.Http.Interfaces
{
    /// <summary>
    /// Wire response returned by <c>POST /auth/players/refresh</c>.
    /// Field names intentionally match the backend JSON contract.
    /// </summary>
    [Serializable]
    public sealed class PlayerRefreshResponseDto
    {
        public string access_token;
        public string refresh_token;
        public string expires_at;
        public int refresh_expires_in;
    }
}
