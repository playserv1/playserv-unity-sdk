namespace Playserv.Events.Requests
{
    /// <summary>
    /// Request to leave a named group.
    /// </summary>
    [System.Serializable]
    public sealed class UnsubscribeGroupRequest
    {
        /// <summary>
        /// Name of the group to leave.
        /// </summary>
        public string GroupName;

        /// <summary>
        /// Creates a group unsubscribe request.
        /// </summary>
        /// <param name="groupName">Name of the group to leave.</param>
        public UnsubscribeGroupRequest(string groupName)
        {
            GroupName = groupName;
        }
    }
}
