#if !PLAYSERV_DISABLE_EVENTS
namespace Playserv.Events.Responses
{
    /// <summary>
    /// Response for a group join request.
    /// </summary>
    [System.Serializable]
    public sealed class SubscribeGroupResponse
    {
        /// <summary>
        /// Whether the group join succeeded.
        /// </summary>
        public bool success;
    }
}

#endif
