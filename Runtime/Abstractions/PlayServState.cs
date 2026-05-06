namespace Playserv.Proxy.Common
{
    /// <summary>
    /// Connection lifecycle states for PlayServ runtime.
    /// </summary>
    public enum PlayServState
    {
        /// <summary>
        /// Disconnected.
        /// </summary>
        Offline,

        /// <summary>
        /// Transport connection is being established.
        /// </summary>
        Connecting,

        /// <summary>
        /// Transport connected, handshake in progress.
        /// </summary>
        Handshaking,

        /// <summary>
        /// Fully connected and operational.
        /// </summary>
        Online,

        /// <summary>
        /// Reconnect attempt is in progress after connection loss.
        /// </summary>
        Reconnecting
    }
}
