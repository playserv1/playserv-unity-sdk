using System.Threading;
using System.Threading.Tasks;
using Playserv.Code;

namespace Playserv.Wrapper
{
    /// <summary>HTTP facade for deployed PlayServ cloud functions.</summary>
    public static class PlayServCode
    {
        /// <summary>
        /// Invokes a function with full control over method, raw/JSON body,
        /// query, tagged version, headers and timeout.
        /// </summary>
        public static Task<PlayServFunctionResult> InvokeAsync(
            PlayServFunctionRequest request,
            CancellationToken cancellationToken = default) =>
            PlayServCodeClient.CreateDefault().InvokeAsync(request, cancellationToken);

        /// <summary>
        /// Invokes a function through the exact-byte transport with bounded buffering and
        /// optional upload/download progress.
        /// </summary>
        public static Task<PlayServFunctionResult> InvokeBytesAsync(
            PlayServFunctionRequest request,
            PlayServFunctionTransferOptions options = null,
            CancellationToken cancellationToken = default) =>
            PlayServCodeClient.CreateDefault().InvokeBytesAsync(
                request,
                options,
                cancellationToken);

        /// <summary>
        /// Streams a function response into a file without buffering the response in memory.
        /// </summary>
        public static Task<PlayServFunctionDownloadResult> DownloadToFileAsync(
            PlayServFunctionRequest request,
            string filePath,
            PlayServFunctionDownloadOptions options = null,
            CancellationToken cancellationToken = default) =>
            PlayServCodeClient.CreateDefault().DownloadToFileAsync(
                request,
                filePath,
                options,
                cancellationToken);

        /// <summary>
        /// POSTs an optional JSON body and deserializes the response. Set options.StrictResponseTypes
        /// to reject response type coercion; unsupported contracts fail before I/O as Deserialization.
        /// </summary>
        public static Task<PlayServFunctionResult<TResponse>> CallAsync<TResponse>(
            string slug,
            object body = null,
            PlayServFunctionCallOptions options = null,
            CancellationToken cancellationToken = default) =>
            PlayServCodeClient.CreateDefault().CallAsync<TResponse>(
                slug,
                body,
                options,
                cancellationToken);

        /// <summary>Strongly typed POST convenience overload.</summary>
        public static Task<PlayServFunctionResult<TResponse>> CallAsync<TRequest, TResponse>(
            string slug,
            TRequest body,
            PlayServFunctionCallOptions options = null,
            CancellationToken cancellationToken = default) =>
            PlayServCodeClient.CreateDefault().CallAsync<TResponse>(
                slug,
                body,
                options,
                cancellationToken);
    }
}
