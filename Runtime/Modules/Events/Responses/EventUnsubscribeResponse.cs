namespace Playserv.Events.Responses
{
    /// <summary>
    /// Response for event unsubscription request.
    /// </summary>
    [System.Serializable]
    public sealed class EventUnsubscribeResponse
    {
        /// <summary>
        /// Indicates whether unsubscribe operation succeeded.
        /// </summary>
        public bool success;
    }
}
