using System.Threading;
using System.Threading.Tasks;
using Playserv.Code;

namespace Playserv.GameServer
{
    /// <summary>Cloud Functions facade authenticated with the configured rotating server key.</summary>
    public sealed class PlayServGameServerCode
    {
        internal PlayServGameServerCode()
        {
        }

        public Task<PlayServFunctionResult> InvokeAsync(
            PlayServFunctionRequest request,
            CancellationToken cancellationToken = default) =>
            PlayServGameServer.CreateCodeClient().InvokeAsync(request, cancellationToken);

        public Task<PlayServFunctionResult<TResponse>> CallAsync<TResponse>(
            string slug,
            object body = null,
            PlayServFunctionCallOptions options = null,
            CancellationToken cancellationToken = default) =>
            PlayServGameServer.CreateCodeClient().CallAsync<TResponse>(
                slug,
                body,
                options,
                cancellationToken);

        public Task<PlayServFunctionResult<TResponse>> CallAsync<TRequest, TResponse>(
            string slug,
            TRequest body,
            PlayServFunctionCallOptions options = null,
            CancellationToken cancellationToken = default) =>
            PlayServGameServer.CreateCodeClient().CallAsync<TResponse>(
                slug,
                body,
                options,
                cancellationToken);
    }
}
