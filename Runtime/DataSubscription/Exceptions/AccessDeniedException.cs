namespace Playserv.DataSubscription.Exceptions
{
    /// <summary>
    /// Indicates that current user has no access to requested subscription target.
    /// </summary>
    public sealed class AccessDeniedException : DataSubscriptionException
    {
        /// <summary>
        /// Creates access denied exception.
        /// </summary>
        /// <param name="message">Backend error message.</param>
        public AccessDeniedException(string message) : base(31001, message)
        {
        }
    }
}
