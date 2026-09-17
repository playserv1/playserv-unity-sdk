using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Playserv.Http.Interfaces
{
    public sealed class PlayServRuntimeHttpException : InvalidOperationException
    {
        public PlayServRuntimeHttpException(string message, int statusCode, string responseBody, string backendCode,
            bool isNetworkError, Exception innerException, string problemTitle, string problemDetail, byte[] responseBytes,
            IReadOnlyDictionary<string, string> responseHeaders)
            : this(message, statusCode, responseBody, backendCode, isNetworkError, innerException, problemTitle, problemDetail, responseBytes)
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (responseHeaders != null) foreach (var header in responseHeaders) headers[header.Key] = header.Value;
            ResponseHeaders = new ReadOnlyDictionary<string, string>(headers);
        }

        public IReadOnlyDictionary<string, string> ResponseHeaders { get; } =
            new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        public PlayServRuntimeHttpException(
            string message,
            int statusCode,
            string responseBody,
            string backendCode,
            bool isNetworkError,
            Exception innerException = null,
            string problemTitle = null,
            string problemDetail = null,
            byte[] responseBytes = null)
            : base(message, innerException)
        {
            StatusCode = statusCode;
            ResponseBody = responseBody ?? string.Empty;
            BackendCode = backendCode ?? string.Empty;
            IsNetworkError = isNetworkError;
            ProblemTitle = problemTitle ?? string.Empty;
            ProblemDetail = problemDetail ?? string.Empty;
            ResponseBytes = responseBytes == null ? Array.Empty<byte>() : (byte[])responseBytes.Clone();
        }

        public int StatusCode { get; }

        public string ResponseBody { get; }

        public string BackendCode { get; }

        public bool IsNetworkError { get; }

        public string ProblemTitle { get; }

        public string ProblemDetail { get; }

        public byte[] ResponseBytes { get; }
    }
}
