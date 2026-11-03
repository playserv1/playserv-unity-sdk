using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace Playserv.GameServer
{
    internal sealed class PlayServJwksResponse
    {
        internal int StatusCode;
        internal string Body;
        internal string CacheControl;
    }

    internal interface IPlayServGameServerJwksTransport
    {
        Task<PlayServJwksResponse> GetAsync(
            string url,
            TimeSpan timeout,
            CancellationToken cancellationToken);
    }

    internal sealed class PlayServUnityGameServerJwksTransport : IPlayServGameServerJwksTransport
    {
        public async Task<PlayServJwksResponse> GetAsync(
            string url,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            using var request = UnityWebRequest.Get(url);
            request.timeout = Math.Max(1, (int)Math.Ceiling(timeout.TotalSeconds));
            var operation = request.SendWebRequest();
            using var deadline = new CancellationTokenSource(timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                deadline.Token);
            try
            {
                while (!operation.isDone)
                {
                    if (linked.IsCancellationRequested)
                    {
                        request.Abort();
                        cancellationToken.ThrowIfCancellationRequested();
                        throw new PlayServGameServerTransportFailure(
                            "The JWKS request timed out.",
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
                request.Abort();
                cancellationToken.ThrowIfCancellationRequested();
                throw new PlayServGameServerTransportFailure(
                    "The JWKS request timed out.",
                    true);
            }

#if UNITY_2020_2_OR_NEWER
            if (request.result == UnityWebRequest.Result.ConnectionError)
#else
            if (request.isNetworkError)
#endif
            {
                var message = string.IsNullOrWhiteSpace(request.error)
                    ? "The JWKS endpoint could not be reached."
                    : request.error;
                throw new PlayServGameServerTransportFailure(
                    message,
                    message.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0);
            }

            return new PlayServJwksResponse
            {
                StatusCode = (int)request.responseCode,
                Body = request.downloadHandler?.text,
                CacheControl = request.GetResponseHeader("Cache-Control")
            };
        }
    }
}
