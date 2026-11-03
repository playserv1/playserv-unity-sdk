using System;
using Playserv.Data;

namespace Playserv.GameServer
{
    /// <summary>
    /// Server Records facade whose mutating requests carry a validated-by-backend
    /// player session token in <c>X-Acting-Player</c>.
    /// </summary>
    public sealed class PlayServGameServerPlayerContext
    {
        private readonly string _playerJwt;

        internal PlayServGameServerPlayerContext(string playerJwt)
        {
            _playerJwt = playerJwt ?? throw new ArgumentNullException(nameof(playerJwt));
        }

        /// <summary>
        /// Opens typed Records whose writes are attributed to the supplied player.
        /// Reads retain the dedicated server's normal owner-bypass scope.
        /// </summary>
        public PlayServRecordSet<T> Records<T>() =>
            PlayServGameServer.CreateRecordSetForActingPlayer<T>(null, _playerJwt);

        /// <summary>
        /// Opens an explicit table whose writes are attributed to the supplied player.
        /// Reads retain the dedicated server's normal owner-bypass scope.
        /// </summary>
        public PlayServRecordSet<T> Records<T>(string entityId)
        {
            if (string.IsNullOrWhiteSpace(entityId))
                throw new ArgumentException("Entity ID is required.", nameof(entityId));
            return PlayServGameServer.CreateRecordSetForActingPlayer<T>(entityId.Trim(), _playerJwt);
        }
    }
}
