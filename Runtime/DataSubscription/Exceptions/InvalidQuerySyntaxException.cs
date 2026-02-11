namespace Playserv.DataSubscription.Exceptions
{
    public sealed class InvalidQuerySyntaxException : DataSubscriptionException
    {
        public InvalidQuerySyntaxException(string message) : base(30001, message)
        {
        }
    }
}
