using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace Playserv.GameServer
{
    internal sealed class PlayServGameServerHttpRequest
    {
        internal string Method;
        internal string RelativePath;
        internal string JsonBody;
        internal string ContentType;
        internal string IfMatch;
        internal string IdempotencyKey;
        internal string FunctionVersion;
        internal IReadOnlyDictionary<string, string> Headers;
        internal string ServerKey;
        internal TimeSpan Timeout;
        internal string Operation;
    }

    internal sealed class PlayServGameServerHttpResponse
    {
        internal int StatusCode;
        internal string Body;
        internal string ETag;
        internal string Location;
        internal string ContentType;
        internal IReadOnlyDictionary<string, string> Headers;
    }

    internal sealed class PlayServGameServerTransportFailure : Exception
    {
        internal PlayServGameServerTransportFailure(string message, bool isTimeout, Exception inner = null)
            : base(message, inner)
        {
            IsTimeout = isTimeout;
        }

        internal bool IsTimeout { get; }
    }

    internal interface IPlayServGameServerTransport
    {
        Task<PlayServGameServerHttpResponse> SendAsync(
            PlayServGameServerHttpRequest request,
            CancellationToken cancellationToken);
    }

    internal sealed class PlayServUnityGameServerTransport : IPlayServGameServerTransport
    {
        private readonly string _baseAddress;

        internal PlayServUnityGameServerTransport(string baseAddress)
        {
            _baseAddress = baseAddress;
        }

        public async Task<PlayServGameServerHttpResponse> SendAsync(
            PlayServGameServerHttpRequest request,
            CancellationToken cancellationToken)
        {
            var url = _baseAddress + "/" + request.RelativePath.TrimStart('/');
            using var webRequest = new UnityWebRequest(url, request.Method)
            {
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = Math.Max(1, (int)Math.Ceiling(request.Timeout.TotalSeconds))
            };

            if (request.JsonBody != null)
            {
                webRequest.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(request.JsonBody))
                {
                    contentType = string.IsNullOrWhiteSpace(request.ContentType)
                        ? "application/json"
                        : request.ContentType
                };
            }

            webRequest.SetRequestHeader("Authorization", "Bearer " + request.ServerKey);
            SetOptionalHeader(webRequest, "If-Match", request.IfMatch);
            SetOptionalHeader(webRequest, "Idempotency-Key", request.IdempotencyKey);
            SetOptionalHeader(webRequest, "X-Playserv-Function-Version", request.FunctionVersion);
            if (request.Headers != null)
            {
                foreach (var header in request.Headers)
                    webRequest.SetRequestHeader(header.Key, header.Value);
            }
            var operation = webRequest.SendWebRequest();
            using var deadline = new CancellationTokenSource(request.Timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                deadline.Token);
            try
            {
                while (!operation.isDone)
                {
                    if (linked.IsCancellationRequested)
                    {
                        TryAbort(webRequest);
                        cancellationToken.ThrowIfCancellationRequested();
                        throw new PlayServGameServerTransportFailure(
                            "The game server request timed out.",
                            true);
                    }
#if UNITY_WEBGL && !UNITY_EDITOR
                    await Task.Yield();
#else
                    await Task.Delay(25, linked.Token);
#endif
                }
            }
            catch (OperationCanceledException)
            {
                TryAbort(webRequest);
                cancellationToken.ThrowIfCancellationRequested();
                throw new PlayServGameServerTransportFailure(
                    "The game server request timed out.",
                    true);
            }

#if UNITY_2020_2_OR_NEWER
            if (webRequest.result == UnityWebRequest.Result.ConnectionError)
#else
            if (webRequest.isNetworkError)
#endif
            {
                var message = string.IsNullOrWhiteSpace(webRequest.error)
                    ? "The game server request could not reach PlayServ."
                    : webRequest.error;
                var timeout = message.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0 ||
                              message.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0;
                throw new PlayServGameServerTransportFailure(message, timeout);
            }

            var headers = webRequest.GetResponseHeaders();
            return new PlayServGameServerHttpResponse
            {
                StatusCode = (int)webRequest.responseCode,
                Body = webRequest.downloadHandler == null ? null : webRequest.downloadHandler.text,
                ETag = webRequest.GetResponseHeader("ETag"),
                Location = webRequest.GetResponseHeader("Location"),
                ContentType = webRequest.GetResponseHeader("Content-Type"),
                Headers = headers == null
                    ? null
                    : new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase)
            };
        }

        private static void SetOptionalHeader(UnityWebRequest request, string name, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                request.SetRequestHeader(name, value);
        }

        private static void TryAbort(UnityWebRequest request)
        {
            try
            {
                request.Abort();
            }
            catch
            {
            }
        }
    }
}
