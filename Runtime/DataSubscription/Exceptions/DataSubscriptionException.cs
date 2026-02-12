using System;

namespace Playserv.DataSubscription.Exceptions
{
    /// <summary>
    /// Base exception for data subscription domain errors.
    /// </summary>
    public class DataSubscriptionException : Exception
    {
        /// <summary>
        /// Numeric error code returned by backend.
        /// </summary>
        public int ErrorCode { get; }

        /// <summary>
        /// Creates data subscription exception.
        /// </summary>
        /// <param name="errorCode">Backend error code.</param>
        /// <param name="message">Error message.</param>
        public DataSubscriptionException(int errorCode, string message) : base(message)
        {
            ErrorCode = errorCode;
        }
    }
}
