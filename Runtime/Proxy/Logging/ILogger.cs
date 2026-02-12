namespace Playserv.Proxy.Logging
{
    /// <summary>
    /// Minimal logger abstraction used by transport/runtime components.
    /// </summary>
    public interface ILogger
    {
        /// <summary>
        /// Writes informational message.
        /// </summary>
        /// <param name="message">Message text.</param>
        void Log(string message);

        /// <summary>
        /// Writes warning message.
        /// </summary>
        /// <param name="message">Message text.</param>
        void LogWarning(string message);

        /// <summary>
        /// Writes error message.
        /// </summary>
        /// <param name="message">Message text.</param>
        void LogError(string message);
    }
}
