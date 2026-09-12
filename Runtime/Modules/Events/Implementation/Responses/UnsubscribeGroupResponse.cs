namespace Playserv.Events.Responses
{
    /// <summary>
    /// Response for a group leave request.
    /// </summary>
    [System.Serializable]
    public sealed class UnsubscribeGroupResponse
    {
        /// <summary>Group name used to correlate the acknowledgement.</summary>
        public string GroupName;

        /// <summary>
        /// Whether the group leave succeeded.
        /// </summary>
        public bool success;

        /// <summary>Backend numeric error code when <see cref="success"/> is false.</summary>
        public int errorCode;

        /// <summary>Backend error message when <see cref="success"/> is false.</summary>
        public string errorMessage;
    }
}
