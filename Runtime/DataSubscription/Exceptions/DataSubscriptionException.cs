using System;

namespace Playserv.DataSubscription.Exceptions
{
    public class DataSubscriptionException : Exception
    {
        public int ErrorCode { get; }

        public DataSubscriptionException(int errorCode, string message) : base(message)
        {
            ErrorCode = errorCode;
        }
    }
}
