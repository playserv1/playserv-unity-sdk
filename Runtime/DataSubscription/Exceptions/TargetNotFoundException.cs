namespace Playserv.DataSubscription.Exceptions
{
    public sealed class TargetNotFoundException : DataSubscriptionException
    {
        public TargetNotFoundException(string message) : base(31002, message)
        {
        }
    }
}
