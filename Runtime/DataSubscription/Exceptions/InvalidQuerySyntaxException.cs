#if !PLAYSERV_DISABLE_DATA && !PLAYSERV_DISABLE_EVENTS
namespace Playserv.DataSubscription.Exceptions
{
    /// <summary>
    /// Indicates malformed subscription query syntax.
    /// </summary>
    public sealed class InvalidQuerySyntaxException : DataSubscriptionException
    {
        /// <summary>
        /// Creates invalid query syntax exception.
        /// </summary>
        /// <param name="message">Backend error message.</param>
        public InvalidQuerySyntaxException(string message) : base(30001, message)
        {
        }
    }
}

#endif
