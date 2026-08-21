using System;
using System.Collections.Generic;

namespace Playserv.Http.Interfaces
{
    public enum PlayServRuntimeTransferDirection
    {
        Upload = 0,
        Download = 1
    }

    public sealed class PlayServRuntimeTransferProgress
    {
        public PlayServRuntimeTransferProgress(
            PlayServRuntimeTransferDirection direction,
            long bytesTransferred,
            long? totalBytes)
        {
            Direction = direction;
            BytesTransferred = bytesTransferred;
            TotalBytes = totalBytes;
        }

        public PlayServRuntimeTransferDirection Direction { get; }

        public long BytesTransferred { get; }

        public long? TotalBytes { get; }

        public double? Fraction => TotalBytes.HasValue && TotalBytes.Value > 0
            ? Math.Min(1d, (double)BytesTransferred / TotalBytes.Value)
            : (double?)null;
    }

    public sealed class PlayServRuntimeBinaryDataRequest
    {
        public PlayServRuntimeDataRequest Request { get; set; }

        public byte[] BodyBytes { get; set; }

        public long MaxResponseBytes { get; set; }

        /// <summary>
        /// Optional temporary destination. When present the response is streamed to this path
        /// and is not retained in <see cref="PlayServRuntimeDataResponse.BodyBytes"/>.
        /// </summary>
        public string DownloadFilePath { get; set; }

        public IProgress<PlayServRuntimeTransferProgress> Progress { get; set; }
    }

    public sealed class PlayServRuntimeDataRequest
    {
        public string Method { get; set; } = "GET";

        public string RelativePath { get; set; } = string.Empty;

        public string ClientToken { get; set; } = string.Empty;

        /// <summary>
        /// Whether the transport must validate and send <c>X-PlayServ-Client</c>.
        /// Keep enabled for runtime APIs; credential-exempt public endpoints may
        /// explicitly disable it.
        /// </summary>
        public bool RequiresClientToken { get; set; } = true;

        public string BearerToken { get; set; } = string.Empty;

        public string JsonBody { get; set; }

        /// <summary>
        /// Content type used when <see cref="JsonBody"/> is present. Defaults
        /// to <c>application/json</c>.
        /// </summary>
        public string ContentType { get; set; }

        public string IdempotencyKey { get; set; }

        public string IfMatch { get; set; }

        /// <summary>
        /// Optional tagged cloud-function version. The HTTP implementation
        /// sends it as <c>X-Playserv-Function-Version</c>.
        /// </summary>
        public string FunctionVersion { get; set; }

        /// <summary>
        /// Additional non-credential request headers. Authorization,
        /// <c>X-Playserv-*</c> and hop-by-hop headers are rejected.
        /// </summary>
        public IReadOnlyDictionary<string, string> Headers { get; set; }

        /// <summary>
        /// Optional timeout override for this request. When omitted, the runtime
        /// HTTP client's configured timeout is used.
        /// </summary>
        public int? TimeoutSeconds { get; set; }
    }

    public sealed class PlayServRuntimeDataResponse
    {
        public PlayServRuntimeDataResponse(
            int statusCode,
            string body,
            string etag,
            string location,
            string contentType = null,
            IReadOnlyDictionary<string, string> headers = null,
            byte[] bodyBytes = null,
            long bytesWritten = 0)
        {
            StatusCode = statusCode;
            Body = body ?? string.Empty;
            ETag = etag ?? string.Empty;
            Location = location ?? string.Empty;
            ContentType = contentType ?? string.Empty;
            var copiedHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (headers != null)
            {
                foreach (var header in headers)
                    copiedHeaders[header.Key] = header.Value;
            }
            Headers = copiedHeaders;
            BodyBytes = bodyBytes == null ? Array.Empty<byte>() : (byte[])bodyBytes.Clone();
            BytesWritten = bytesWritten;
        }

        public int StatusCode { get; }

        public string Body { get; }

        public string ETag { get; }

        public string Location { get; }

        public string ContentType { get; }

        public IReadOnlyDictionary<string, string> Headers { get; }

        public byte[] BodyBytes { get; }

        public long BytesWritten { get; }
    }
}
