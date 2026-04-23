using System;
using Newtonsoft.Json.Linq;
using Playserv.DataSubscription.Exceptions;
using Playserv.DataSubscription.Responses;

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
                _ => new DataSubscriptionException(error.ErrorCode, message)
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

        public static JToken ExtractSubscriptionPayload(JToken data, string rootFieldName)
        {
            if (data == null)
                return null;

            if (data is not JObject obj)
                return data;

            if (!string.IsNullOrWhiteSpace(rootFieldName))
            {
                if (obj.TryGetValue(rootFieldName, StringComparison.Ordinal, out var exact))
                    return exact;

                if (obj.TryGetValue(rootFieldName, StringComparison.OrdinalIgnoreCase, out var insensitive))
                    return insensitive;
            }

            foreach (var property in obj.Properties())
            {
                return property.Value;
            }

            return data;
        }
    }
}
