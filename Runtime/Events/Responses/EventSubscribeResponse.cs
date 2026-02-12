namespace Playserv.Events.Responses
{
    /// <summary>
    /// Response for successful event subscription creation.
    /// </summary>
    [System.Serializable]
    public sealed class EventSubscribeResponse
    {
        /// <summary>
        /// Created event subscription id.
        /// </summary>
        public string eventSubscriptionId;
    }
}



