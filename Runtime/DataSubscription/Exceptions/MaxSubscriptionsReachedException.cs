namespace Playserv.DataSubscription.Exceptions
{
    /// <summary>
    /// Indicates that subscription limit was reached for current session/user.
    /// </summary>
    public sealed class MaxSubscriptionsReachedException : DataSubscriptionException
    {
        /// <summary>
        /// Creates max subscriptions reached exception.
        /// </summary>
        /// <param name="message">Backend error message.</param>
        public MaxSubscriptionsReachedException(string message) : base(39001, message)
        {
        }
    }
}
