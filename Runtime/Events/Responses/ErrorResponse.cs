#if !PLAYSERV_DISABLE_EVENTS
namespace Playserv.Events.Responses
{
    /// <summary>
    /// Generic error response from events module.
    /// </summary>
    [System.Serializable]
    public sealed class ErrorResponse
    {
        /// <summary>
        /// Numeric error code.
        /// </summary>
        public int ErrorCode;

        /// <summary>
        /// Human-readable error message.
        /// </summary>
        public string Message;
    }
}




#endif
