namespace Playserv.Events.Responses
{
    /// <summary>
    /// Response for a group join request.
    /// </summary>
    [System.Serializable]
    public sealed class SubscribeGroupResponse
    {
        /// <summary>Group name used to correlate the acknowledgement.</summary>
        public string GroupName;

        /// <summary>
        /// Whether the group join succeeded.
        /// </summary>
        public bool success;

        /// <summary>Backend numeric error code when <see cref="success"/> is false.</summary>
        public int errorCode;

        /// <summary>Backend error message when <see cref="success"/> is false.</summary>
        public string errorMessage;
    }
}
