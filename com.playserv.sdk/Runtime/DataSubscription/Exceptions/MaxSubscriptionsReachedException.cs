namespace Playserv.DataSubscription.Exceptions
{
    public sealed class MaxSubscriptionsReachedException : DataSubscriptionException
    {
        public MaxSubscriptionsReachedException(string message) : base(39001, message)
        {
        }
    }
}
