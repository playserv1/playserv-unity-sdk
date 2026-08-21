using System;
using Playserv.Wrapper;

namespace Playserv.RPC
{
    /// <summary>
    /// Stable categories for awaitable RPC failures.
    /// </summary>
    public enum PlayServRpcErrorCode
    {
        None = 0,
        SerializationFailed = 1,
        TransportFailed = 2,
        Timeout = 3,
        Canceled = 4,
        ServerError = 5,
        InvalidResponse = 6,
        DeserializationFailed = 7
    }

    /// <summary>
    /// Structured failure returned by an awaitable RPC invocation.
    /// </summary>
    public sealed class PlayServRpcError
    {
        internal PlayServRpcError(
            PlayServRpcErrorCode code,
            string message,
            string status,
            Exception exception,
            string rawDetails = null)
        {
            Code = code;
            Message = message ?? string.Empty;
            Status = status ?? string.Empty;
            Exception = exception;
            UnifiedError = CreateUnifiedError(code, Message, Status, rawDetails);
        }

        public PlayServRpcErrorCode Code { get; }

        public string Message { get; }

        public string Status { get; }

        public Exception Exception { get; }

        /// <summary>Cross-module representation of this RPC failure.</summary>
        public PlayServError UnifiedError { get; }

        public override string ToString()
        {
            return string.IsNullOrWhiteSpace(Status)
                ? $"{Code}: {Message}"
                : $"{Code} ({Status}): {Message}";
        }

        private static PlayServError CreateUnifiedError(
            PlayServRpcErrorCode code,
            string message,
            string status,
            string rawDetails)
        {
            var commonCode = code switch
            {
                PlayServRpcErrorCode.None => PlayServErrorCode.None,
                PlayServRpcErrorCode.SerializationFailed => PlayServErrorCode.Serialization,
                PlayServRpcErrorCode.TransportFailed => PlayServErrorCode.Transport,
                PlayServRpcErrorCode.Timeout => PlayServErrorCode.Timeout,
                PlayServRpcErrorCode.Canceled => PlayServErrorCode.Canceled,
                PlayServRpcErrorCode.ServerError => PlayServErrorCode.ServerError,
                PlayServRpcErrorCode.InvalidResponse => PlayServErrorCode.InvalidResponse,
                PlayServRpcErrorCode.DeserializationFailed => PlayServErrorCode.Deserialization,
                _ => PlayServErrorCode.Unknown
            };
            return new PlayServError(
                commonCode,
                string.IsNullOrWhiteSpace(status) ? code.ToString() : status,
                message,
                retryable: code == PlayServRpcErrorCode.TransportFailed ||
                           code == PlayServRpcErrorCode.Timeout ||
                           code == PlayServRpcErrorCode.ServerError,
                rawDetails: rawDetails);
        }
    }

    /// <summary>
    /// Typed result of an awaitable RPC invocation.
    /// </summary>
    /// <typeparam name="T">Expected response payload type.</typeparam>
    public sealed class PlayServRpcResult<T>
    {
        internal PlayServRpcResult(
            string requestId,
            T value,
            string rawResult,
            string status,
            string message,
            DateTimeOffset timestamp,
            PlayServRpcError error)
        {
            RequestId = requestId ?? string.Empty;
            Value = value;
            RawResult = rawResult;
            Status = status ?? string.Empty;
            Message = message ?? string.Empty;
            Timestamp = timestamp;
            Error = error;
        }

        public string RequestId { get; }

        public bool IsSuccess => Error == null;

        public T Value { get; }

        public string RawResult { get; }

        public string Status { get; }

        public string Message { get; }

        public DateTimeOffset Timestamp { get; }

        public PlayServRpcError Error { get; }

        /// <summary>Cross-module failure, or <c>null</c> when the invocation succeeded.</summary>
        public PlayServError UnifiedError => Error?.UnifiedError;
    }
}
