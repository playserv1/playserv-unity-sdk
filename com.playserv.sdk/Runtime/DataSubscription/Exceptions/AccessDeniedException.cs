namespace Playserv.DataSubscription.Exceptions
{
    public sealed class AccessDeniedException : DataSubscriptionException
    {
        public AccessDeniedException(string message) : base(31001, message)
        {
        }
    }
}
