using System;
using Playserv.Wrapper;

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
        public DataSubscriptionException(
            int errorCode,
            string message,
            bool? retryable = null,
            string sourceCode = null,
            string rawDetails = null) : base(message)
        {
            ErrorCode = errorCode;
            var commonCode = MapCode(errorCode, message);
            var effectiveRetryable = IsTerminal(commonCode)
                ? false
                : retryable ?? IsRetryable(commonCode);
            UnifiedError = new PlayServError(
                commonCode,
                string.IsNullOrWhiteSpace(sourceCode)
                    ? errorCode > 0 ? $"dataflow_{errorCode}" : "subscription_error"
                    : sourceCode,
                message,
                transportCode: errorCode > 0 ? (int?)errorCode : null,
                retryable: effectiveRetryable,
                rawDetails: rawDetails);
        }

        /// <summary>Cross-module representation of this subscription failure.</summary>
        public PlayServError UnifiedError { get; }

        private static PlayServErrorCode MapCode(int errorCode, string message)
        {
            switch (errorCode)
            {
                case 30001:
                    return PlayServErrorCode.Validation;
                case 31001:
                    return PlayServErrorCode.Forbidden;
                case 31002:
                case 41001:
                    return PlayServErrorCode.NotFound;
                case 39001:
                    return PlayServErrorCode.RateLimited;
                case 39002:
                    return PlayServErrorCode.Validation;
                case 40001:
                    return PlayServErrorCode.InvalidResponse;
                case 49001:
                    return PlayServErrorCode.SubscriptionTerminated;
            }

            if (!string.IsNullOrWhiteSpace(message) &&
                message.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return PlayServErrorCode.Timeout;
            }

            if (!string.IsNullOrWhiteSpace(message) &&
                (message.IndexOf("offline", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 message.IndexOf("connection", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 message.IndexOf("transport", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return PlayServErrorCode.Network;
            }

            return PlayServErrorCode.Unknown;
        }

        private static bool IsRetryable(PlayServErrorCode code)
        {
            return code == PlayServErrorCode.Network ||
                   code == PlayServErrorCode.Timeout ||
                   code == PlayServErrorCode.ServerError;
        }

        private static bool IsTerminal(PlayServErrorCode code) =>
            code == PlayServErrorCode.Validation ||
            code == PlayServErrorCode.Unauthorized ||
            code == PlayServErrorCode.Forbidden ||
            code == PlayServErrorCode.NotFound ||
            code == PlayServErrorCode.Conflict ||
            code == PlayServErrorCode.SubscriptionTerminated;
    }
}
