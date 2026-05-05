#if !PLAYSERV_DISABLE_EVENTS
namespace Playserv.Events.Requests
{
    /// <summary>
    /// Request to join a named group for group-scoped event routing.
    /// </summary>
    [System.Serializable]
    public sealed class SubscribeGroupRequest
    {
        /// <summary>
        /// Name of the group to join.
        /// </summary>
        public string GroupName;

        /// <summary>
        /// Creates a group subscribe request.
        /// </summary>
        /// <param name="groupName">Name of the group to join.</param>
        public SubscribeGroupRequest(string groupName)
        {
            GroupName = groupName;
        }
    }
}

#endif
