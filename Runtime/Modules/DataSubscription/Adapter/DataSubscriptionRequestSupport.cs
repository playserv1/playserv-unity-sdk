#if !PLAYSERV_DISABLE_DATA && !PLAYSERV_DISABLE_EVENTS
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Playserv.DataSubscription.Requests;
using Playserv.DataSubscription.Responses;
using Playserv.Proxy.Common;
using Playserv.Serialization;

namespace Playserv.DataSubscription
{
    internal static class DataSubscriptionRequestSupport
    {
        public static Dictionary<string, object> CloneVariables(Dictionary<string, object> variables)
        {
            if (variables == null || variables.Count == 0)
                return new Dictionary<string, object>();

            return new Dictionary<string, object>(variables);
        }

        public static bool TryMapDataGetResponse(IJsonCodec jsonCodec, object command, out DataGetResponse response)
        {
            if (jsonCodec == null)
                throw new ArgumentNullException(nameof(jsonCodec));

            response = null;
            if (command == null)
                return false;

            if (command is DataGetResponse dataGetResponse)
            {
                NormalizeDataGetResponse(jsonCodec, dataGetResponse);
                response = dataGetResponse;
                return true;
            }

            try
            {
                var mapped = jsonCodec.Convert<DataGetResponse>(command);
                if (mapped == null)
                    return false;

                NormalizeDataGetResponse(jsonCodec, mapped);
                response = mapped;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool TryMapDataSubscriptionResponse(IJsonCodec jsonCodec, object command, out DataSubscriptionResponse response)
        {
            if (jsonCodec == null)
                throw new ArgumentNullException(nameof(jsonCodec));

            response = null;
            if (command == null)
                return false;

            if (command is DataSubscriptionResponse typed)
            {
                response = typed;
                return true;
            }

            try
            {
                var mapped = jsonCodec.Convert<DataSubscriptionResponse>(command);
                if (mapped == null)
                    return false;

                response = mapped;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool LooksLikeDataSubscriptionCommandError(CommandErrorResponse response)
        {
            var error = response?.Error ?? string.Empty;
            var message = response?.Message ?? string.Empty;

            return message.IndexOf("DataSubscriptionRequest", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("DataSubscription", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   error.IndexOf("DataSubscriptionRequest", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   error.IndexOf("DataSubscription", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool LooksLikeDataGetCommandError(CommandErrorResponse response)
        {
            var error = response?.Error ?? string.Empty;
            var message = response?.Message ?? string.Empty;

            return message.IndexOf("DataGetRequest", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("DataGet", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("DataGetResponse", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   error.IndexOf("DataGetRequest", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static DataGetResponse CreateDataGetErrorResponse(long requestId, int errorCode, string message)
        {
            return new DataGetResponse
            {
                RequestId = requestId,
                Error = new DataGetError
                {
                    Code = errorCode,
                    Message = message ?? "Unknown data get error."
                }
            };
        }

        public static DataSubscriptionResponse CreateDataSubscriptionErrorResponse(long requestId, int errorCode, string message)
        {
            return new DataSubscriptionResponse
            {
                RequestId = requestId,
                Error = new DataSubscriptionError
                {
                    ErrorCode = errorCode,
                    Message = message ?? "Unknown data subscription error."
                }
            };
        }

        public static async Task<bool> WaitForCompletionOrTimeoutAsync(Task task, int timeoutMs, CancellationToken ct)
        {
            if (task.IsCompleted)
                return true;

            if (timeoutMs <= 0)
                timeoutMs = 1;

#if UNITY_WEBGL && !UNITY_EDITOR
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            while (!task.IsCompleted)
            {
                if (ct.IsCancellationRequested)
                    throw new OperationCanceledException(ct);

                if (stopwatch.ElapsedMilliseconds >= timeoutMs)
                    return false;

                await Task.Yield();
            }

            return true;
#else
            var timeoutTask = Task.Delay(timeoutMs, ct);
            var completedTask = await Task.WhenAny(task, timeoutTask);
            if (completedTask == task)
                return true;

            if (ct.IsCancellationRequested)
                throw new OperationCanceledException(ct);

            return false;
#endif
        }

        public static DataGetRequest CreateDataGetRequest(
            DataSubscriptionRequestIdSource requestIds,
            string key,
            string query,
            Dictionary<string, object> variables)
        {
            if (requestIds == null)
                throw new ArgumentNullException(nameof(requestIds));

            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Key is required.", nameof(key));

            if (string.IsNullOrWhiteSpace(query))
                throw new ArgumentException("Query is required.", nameof(query));

            return new DataGetRequest
            {
                RequestId = requestIds.Next(),
                Key = key,
                Query = query,
                Variables = CloneVariables(variables)
            };
        }

        private static void NormalizeDataGetResponse(IJsonCodec jsonCodec, DataGetResponse response)
        {
            if (jsonCodec == null)
                throw new ArgumentNullException(nameof(jsonCodec));

            if (response?.Result == null || response.Result.Data == null)
                return;

            response.Result.Data = jsonCodec.ToPlainValue(response.Result.Data);
        }
    }
}

#endif
