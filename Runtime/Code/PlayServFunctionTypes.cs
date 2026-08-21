using System;
using System.Collections.Generic;
using Playserv.Wrapper;

namespace Playserv.Code
{
    /// <summary>HTTP verbs accepted by the PlayServ cloud-function gateway.</summary>
    public enum PlayServFunctionMethod
    {
        Get,
        Post,
        Put,
        Patch,
        Delete
    }

    /// <summary>Optional settings for a typed POST cloud-function call.</summary>
    public sealed class PlayServFunctionCallOptions
    {
        /// <summary>Optional live tagged deploy selected by the gateway.</summary>
        public string Version { get; set; }

        public IReadOnlyDictionary<string, string> Query { get; set; }

        /// <summary>
        /// Non-credential pass-through headers. Authorization and
        /// <c>X-Playserv-*</c> headers are reserved and rejected.
        /// </summary>
        public IReadOnlyDictionary<string, string> Headers { get; set; }

        public int? TimeoutSeconds { get; set; }
    }

    /// <summary>Raw request passed to <see cref="PlayServCode.InvokeAsync"/>.</summary>
    public sealed class PlayServFunctionRequest
    {
        public string Slug { get; set; }

        public PlayServFunctionMethod Method { get; set; } = PlayServFunctionMethod.Post;

        public IReadOnlyDictionary<string, string> Query { get; set; }

        /// <summary>Value serialized as JSON. Mutually exclusive with <see cref="RawBody"/>.</summary>
        public object Body { get; set; }

        /// <summary>Already serialized request body. Mutually exclusive with <see cref="Body"/>.</summary>
        public string RawBody { get; set; }

        /// <summary>
        /// Exact request bytes. Mutually exclusive with <see cref="Body"/> and
        /// <see cref="RawBody"/>. The SDK never logs this payload.
        /// </summary>
        public byte[] RawBodyBytes { get; set; }

        public string ContentType { get; set; } = "application/json";

        public string Version { get; set; }

        public IReadOnlyDictionary<string, string> Headers { get; set; }

        public int? TimeoutSeconds { get; set; }
    }

    /// <summary>Raw response returned by a PlayServ cloud function.</summary>
    public sealed class PlayServFunctionResponse
    {
        internal PlayServFunctionResponse(
            int statusCode,
            string body,
            string contentType,
            IReadOnlyDictionary<string, string> headers,
            byte[] bodyBytes = null)
        {
            StatusCode = statusCode;
            Body = body ?? string.Empty;
            ContentType = contentType ?? string.Empty;
            var copiedHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (headers != null)
            {
                foreach (var header in headers)
                    copiedHeaders[header.Key] = header.Value;
            }
            Headers = copiedHeaders;
            BodyBytes = bodyBytes == null ? Array.Empty<byte>() : (byte[])bodyBytes.Clone();
        }

        public int StatusCode { get; }

        public string Body { get; }

        public string ContentType { get; }

        public IReadOnlyDictionary<string, string> Headers { get; }

        /// <summary>
        /// Exact response bytes for buffered invocations. File downloads leave this empty.
        /// Use <see cref="Body"/> only for text or JSON content.
        /// </summary>
        public byte[] BodyBytes { get; }
    }

    public enum PlayServFunctionTransferDirection
    {
        Upload = 0,
        Download = 1
    }

    public sealed class PlayServFunctionTransferProgress
    {
        internal PlayServFunctionTransferProgress(
            PlayServFunctionTransferDirection direction,
            long bytesTransferred,
            long? totalBytes)
        {
            Direction = direction;
            BytesTransferred = bytesTransferred;
            TotalBytes = totalBytes;
        }

        public PlayServFunctionTransferDirection Direction { get; }

        public long BytesTransferred { get; }

        public long? TotalBytes { get; }

        public double? Fraction => TotalBytes.HasValue && TotalBytes.Value > 0
            ? Math.Min(1d, (double)BytesTransferred / TotalBytes.Value)
            : (double?)null;
    }

    public sealed class PlayServFunctionTransferOptions
    {
        public const long DefaultMaxResponseBytes = 16L * 1024L * 1024L;

        public long MaxResponseBytes { get; set; } = DefaultMaxResponseBytes;

        public IProgress<PlayServFunctionTransferProgress> Progress { get; set; }
    }

    public sealed class PlayServFunctionDownloadOptions
    {
        public const long DefaultMaxResponseBytes = 512L * 1024L * 1024L;

        public long MaxResponseBytes { get; set; } = DefaultMaxResponseBytes;

        public bool OverwriteExistingFile { get; set; }

        public IProgress<PlayServFunctionTransferProgress> Progress { get; set; }
    }

    /// <summary>Result of a raw cloud-function invocation.</summary>
    public sealed class PlayServFunctionResult
    {
        internal PlayServFunctionResult(
            PlayServFunctionResponse response,
            PlayServError error)
        {
            Response = response;
            Error = error ?? PlayServError.None;
        }

        public bool IsSuccess => !Error.IsError;

        public PlayServFunctionResponse Response { get; }

        public PlayServError Error { get; }
    }

    /// <summary>Result of a typed cloud-function invocation.</summary>
    public sealed class PlayServFunctionResult<T>
    {
        internal PlayServFunctionResult(
            T value,
            PlayServFunctionResponse response,
            PlayServError error)
        {
            Value = value;
            Response = response;
            Error = error ?? PlayServError.None;
        }

        public bool IsSuccess => !Error.IsError;

        public T Value { get; }

        public PlayServFunctionResponse Response { get; }

        public PlayServError Error { get; }
    }

    public sealed class PlayServFunctionDownloadResult
    {
        internal PlayServFunctionDownloadResult(
            string filePath,
            long bytesWritten,
            PlayServFunctionResponse response,
            PlayServError error)
        {
            FilePath = filePath ?? string.Empty;
            BytesWritten = bytesWritten;
            Response = response;
            Error = error ?? PlayServError.None;
        }

        public bool IsSuccess => !Error.IsError;

        public string FilePath { get; }

        public long BytesWritten { get; }

        public PlayServFunctionResponse Response { get; }

        public PlayServError Error { get; }
    }
}
