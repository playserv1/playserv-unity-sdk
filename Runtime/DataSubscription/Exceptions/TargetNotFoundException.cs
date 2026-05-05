#if !PLAYSERV_DISABLE_DATA && !PLAYSERV_DISABLE_EVENTS
namespace Playserv.DataSubscription.Exceptions
{
    /// <summary>
    /// Indicates that requested entity target was not found.
    /// </summary>
    public sealed class TargetNotFoundException : DataSubscriptionException
    {
        /// <summary>
        /// Creates target not found exception.
        /// </summary>
        /// <param name="message">Backend error message.</param>
        public TargetNotFoundException(string message) : base(31002, message)
        {
        }
    }
}

#endif
