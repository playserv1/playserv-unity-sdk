using System;
using System.Text.RegularExpressions;

namespace Playserv.Wrapper
{
    /// <summary>
    /// Stable cross-module categories used by <see cref="PlayServError"/>.
    /// Source-specific codes remain available through <see cref="PlayServError.SourceCode"/>.
    /// </summary>
    public enum PlayServErrorCode
    {
        None = 0,
        Unknown = 1,
        InvalidConfiguration = 2,
        Validation = 3,
        Unauthorized = 4,
        Forbidden = 5,
        NotFound = 6,
        Conflict = 7,
        RateLimited = 8,
        Serialization = 9,
        Deserialization = 10,
        InvalidResponse = 11,
        Persistence = 12,
        Network = 13,
        Timeout = 14,
        Canceled = 15,
        Transport = 16,
        ServerError = 17,
        SubscriptionTerminated = 18
    }

    /// <summary>
    /// Immutable error shared by transport, authentication, data, subscriptions and RPC.
    /// </summary>
    public sealed class PlayServError
    {
        private static readonly Regex SensitiveJsonValue = new Regex(
            "(\\\"(?:authorization|access[_-]?token|refresh[_-]?token|provider[_-]?token|id[_-]?token|client[_-]?token|token|jwt|credential|password|secret)\\\"\\s*:\\s*)\\\"(?:\\\\.|[^\\\"])*\\\"",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex BearerValue = new Regex(
            "\\bBearer\\s+[^\\s,;\\\"]+",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex SensitiveAssignment = new Regex(
            "\\b(authorization|access[_-]?token|refresh[_-]?token|provider[_-]?token|id[_-]?token|client[_-]?token|token|jwt|credential|password|secret)\\b\\s*[=:]\\s*[^&\\s,;\\\"}]+",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex JwtValue = new Regex(
            "\\beyJ[A-Za-z0-9_-]+\\.[A-Za-z0-9_-]+\\.[A-Za-z0-9_-]*\\b",
            RegexOptions.CultureInvariant);

        public static readonly PlayServError None = new PlayServError(
            PlayServErrorCode.None,
            string.Empty,
            string.Empty);

        public PlayServError(
            PlayServErrorCode code,
            string sourceCode,
            string message,
            int? httpStatus = null,
            int? transportCode = null,
            bool retryable = false,
            string rawDetails = null)
        {
            Code = code;
            SourceCode = sourceCode ?? string.Empty;
            Message = message ?? string.Empty;
            HttpStatus = httpStatus;
            TransportCode = transportCode;
            Retryable = retryable;
            RawDetails = SanitizeRawDetails(rawDetails);
        }

        public PlayServErrorCode Code { get; }

        /// <summary>Exact backend code or legacy SDK enum name when available.</summary>
        public string SourceCode { get; }

        public string Message { get; }

        public int? HttpStatus { get; }

        /// <summary>Numeric WebSocket, handshake or dataflow error code.</summary>
        public int? TransportCode { get; }

        public bool Retryable { get; }

        /// <summary>Original non-secret error payload. The SDK never logs this automatically.</summary>
        public string RawDetails { get; }

        public bool IsError => Code != PlayServErrorCode.None;

        public static PlayServError FromHttp(
            int status,
            string sourceCode,
            string message,
            bool isNetworkError,
            bool isTimeout,
            string rawDetails = null,
            bool? retryable = null)
        {
            var code = isNetworkError
                ? isTimeout ? PlayServErrorCode.Timeout : PlayServErrorCode.Network
                : ClassifyHttpStatus(status);
            var effectiveRetryable = IsTerminal(code)
                ? false
                : retryable ?? IsHttpRetryable(status, isNetworkError, isTimeout);
            return new PlayServError(
                code,
                string.IsNullOrWhiteSpace(sourceCode) ? HttpSourceCode(status) : sourceCode,
                message,
                status > 0 ? (int?)status : null,
                null,
                effectiveRetryable,
                rawDetails);
        }

        public static PlayServError FromTransport(
            int transportCode,
            string sourceCode,
            string message,
            bool retryable = false,
            string rawDetails = null,
            PlayServErrorCode code = PlayServErrorCode.Transport)
        {
            return new PlayServError(
                code,
                sourceCode,
                message,
                null,
                transportCode,
                retryable,
                rawDetails);
        }

        public override string ToString()
        {
            var source = string.IsNullOrWhiteSpace(SourceCode) ? string.Empty : $" ({SourceCode})";
            var http = HttpStatus.HasValue ? $" HTTP {HttpStatus.Value}" : string.Empty;
            var transport = TransportCode.HasValue ? $" WS {TransportCode.Value:D5}" : string.Empty;
            return $"{Code}{source}{http}{transport}: {Message}";
        }

        private static PlayServErrorCode ClassifyHttpStatus(int status)
        {
            switch (status)
            {
                case 400:
                case 422:
                    return PlayServErrorCode.Validation;
                case 401:
                    return PlayServErrorCode.Unauthorized;
                case 403:
                    return PlayServErrorCode.Forbidden;
                case 404:
                    return PlayServErrorCode.NotFound;
                case 408:
                    return PlayServErrorCode.Timeout;
                case 409:
                case 412:
                    return PlayServErrorCode.Conflict;
                case 425:
                case 429:
                    return PlayServErrorCode.RateLimited;
                default:
                    if (status >= 500)
                        return PlayServErrorCode.ServerError;
                    return status > 0 ? PlayServErrorCode.Unknown : PlayServErrorCode.InvalidResponse;
            }
        }

        private static bool IsHttpRetryable(int status, bool isNetworkError, bool isTimeout)
        {
            return isNetworkError || isTimeout || status == 408 || status == 425 || status == 429 || status >= 500;
        }

        private static bool IsTerminal(PlayServErrorCode code) =>
            code == PlayServErrorCode.InvalidConfiguration ||
            code == PlayServErrorCode.Validation ||
            code == PlayServErrorCode.Unauthorized ||
            code == PlayServErrorCode.Forbidden ||
            code == PlayServErrorCode.NotFound ||
            code == PlayServErrorCode.Conflict ||
            code == PlayServErrorCode.Canceled ||
            code == PlayServErrorCode.SubscriptionTerminated;

        private static string HttpSourceCode(int status) =>
            status > 0 ? $"http_{status}" : "invalid_http_response";

        private static string SanitizeRawDetails(string rawDetails)
        {
            if (string.IsNullOrEmpty(rawDetails))
                return string.Empty;

            var sanitized = SensitiveJsonValue.Replace(rawDetails, "$1\"[REDACTED]\"");
            sanitized = BearerValue.Replace(sanitized, "Bearer [REDACTED]");
            sanitized = SensitiveAssignment.Replace(sanitized, "$1=[REDACTED]");
            return JwtValue.Replace(sanitized, "[REDACTED_JWT]");
        }
    }
}
