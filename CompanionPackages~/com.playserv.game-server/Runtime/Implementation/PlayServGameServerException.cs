using System;
using Playserv.Wrapper;

namespace Playserv.GameServer
{
    /// <summary>An HTTP, network, response, or backend failure with a normalized PlayServ error.</summary>
    public sealed class PlayServGameServerException : Exception
    {
        public PlayServGameServerException(PlayServError unifiedError, Exception innerException = null)
            : base((unifiedError ?? throw new ArgumentNullException(nameof(unifiedError))).Message, innerException)
        {
            UnifiedError = unifiedError;
        }

        public PlayServError UnifiedError { get; }
    }
}
