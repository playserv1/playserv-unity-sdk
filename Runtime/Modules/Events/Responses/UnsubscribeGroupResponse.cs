#if !PLAYSERV_DISABLE_EVENTS
namespace Playserv.Events.Responses
{
    /// <summary>
    /// Response for a group leave request.
    /// </summary>
    [System.Serializable]
    public sealed class UnsubscribeGroupResponse
    {
        /// <summary>
        /// Whether the group leave succeeded.
        /// </summary>
        public bool success;
    }
}

#endif
