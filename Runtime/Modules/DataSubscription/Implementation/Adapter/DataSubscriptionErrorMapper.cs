using System;
using Playserv.DataSubscription.Exceptions;
using Playserv.DataSubscription.Responses;
using Playserv.Serialization;

namespace Playserv.DataSubscription
{
    internal static class DataSubscriptionErrorMapper
    {
        public static DataSubscriptionException MapDataGetError(DataGetError error)
        {
            if (error == null)
                return new DataSubscriptionException(0, "Unknown data get error.");

            var code = error.Code;
            var message = error.Message ?? "Unknown data get error.";

            return code switch
            {
                31002 => new TargetNotFoundException(message),
                SubscriptionNotFoundException.Code => new SubscriptionNotFoundException(0, message),
                31001 => new AccessDeniedException(message),
                _ => new DataSubscriptionException(code, message)
            };
        }

        public static DataSubscriptionException MapErrorToException(DataSubscriptionError error)
        {
            if (error == null)
                return new DataSubscriptionException(0, "Unknown data subscription error.");

            var message = error.Message ?? "Unknown data subscription error.";

            return error.ErrorCode switch
            {
                30001 => new InvalidQuerySyntaxException(message),
                31001 => new AccessDeniedException(message),
                31002 => new TargetNotFoundException(message),
                39001 => new MaxSubscriptionsReachedException(message),
                UpdateDataCorruptionException.Code => new UpdateDataCorruptionException(message),
                SubscriptionNotFoundException.Code => new SubscriptionNotFoundException(0, message),
                SubscriptionTerminatedException.Code => new SubscriptionTerminatedException(0, message),
                _ => new DataSubscriptionException(
                    error.ErrorCode,
                    message,
                    error.Retryable,
                    error.SourceCode,
                    error.RawDetails)
            };
        }

        public static bool ShouldFallbackToPolling(DataSubscriptionException exception)
        {
            if (exception == null)
                return true;

            switch (exception.ErrorCode)
            {
                case 30001:
                case 31001:
                case 31002:
                case 39001:
                case UpdateDataCorruptionException.Code:
                case SubscriptionNotFoundException.Code:
                case SubscriptionTerminatedException.Code:
                    return false;
            }

            var message = exception.Message ?? string.Empty;
            if (string.IsNullOrWhiteSpace(message))
                return exception.ErrorCode == 0;

            return message.IndexOf("not supported", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("unknown command", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("DataSubscriptionRequest", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("Timed out waiting for DataSubscriptionResponse", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static DataSubscriptionException MapDataMutationError(DataMutationError error)
        {
            if (error == null)
                return new DataSubscriptionException(0, "Unknown data mutation error.");

            var code = error.Code;
            var message = error.Message ?? "Unknown data mutation error.";

            return code switch
            {
                31001 => new AccessDeniedException(message),
                31002 => new TargetNotFoundException(message),
                SubscriptionNotFoundException.Code => new SubscriptionNotFoundException(0, message),
                UpdateDataCorruptionException.Code => new UpdateDataCorruptionException(message),
                _ => new DataSubscriptionException(code, message)
            };
        }

        public static object ExtractSubscriptionPayload(object data, string rootFieldName, IJsonCodec jsonCodec)
        {
            if (jsonCodec == null)
                throw new ArgumentNullException(nameof(jsonCodec));

            if (data == null)
                return null;

            object firstPropertyValue;
            if (!jsonCodec.TryGetFirstPropertyValue(data, out firstPropertyValue))
                return data;

            if (!string.IsNullOrWhiteSpace(rootFieldName))
            {
                if (jsonCodec.TryGetProperty(data, rootFieldName, ignoreCase: false, out var exact))
                    return exact;

                if (jsonCodec.TryGetProperty(data, rootFieldName, ignoreCase: true, out var insensitive))
                    return insensitive;
            }

            return firstPropertyValue;
        }
    }
}
