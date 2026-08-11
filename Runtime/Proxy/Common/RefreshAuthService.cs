using System;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Proxy.Interfaces;
using Playserv.Proxy.Logging;
using Playserv.Runtime.Abstractions;

namespace Playserv.Proxy.Common
{
    /// <summary>
    /// Pushes a rotated player credential into the live transport connection.
    /// </summary>
    public sealed class RefreshAuthService : IDisposable
    {
        private const int RefreshTimeoutMs = 20000;

        private readonly ITransport _transport;
        private readonly ILogger _logger;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private IDisposable _responseSubscription;
        private TaskCompletionSource<bool> _refreshTcs;
        private bool _disposed;

        public RefreshAuthService(ITransport transport, ILogger logger)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<bool> RefreshAsync(
            string playerAccessToken,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            var authorization = PlayServCredentialPolicy.NormalizePlayerAuthorization(playerAccessToken);
            if (authorization == null)
                throw new ArgumentException("Player access token is required.", nameof(playerAccessToken));

            await _gate.WaitAsync(cancellationToken);
            try
            {
                ThrowIfDisposed();
                var refreshTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _refreshTcs = refreshTcs;

                // Transport subscriptions are cleared by ResetConnection, so renew this subscription per request.
                _responseSubscription?.Dispose();
                _responseSubscription = _transport
                    .OnReceive<RefreshAuthResponse>()
                    .Subscribe(OnRefreshAuthResponse, OnRefreshAuthError, null);

                _logger.Log("Sending RefreshAuthRequest.");
                await _transport.Send(new RefreshAuthRequest
                {
                    Authorization = authorization
                });

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(RefreshTimeoutMs);

                try
                {
                    var completedInTime = await AsyncTimeoutHelper.WaitForCompletionOrTimeoutAsync(
                        refreshTcs.Task,
                        RefreshTimeoutMs,
                        timeoutCts.Token);
                    if (!completedInTime && !refreshTcs.Task.IsCompleted)
                    {
                        _logger.LogError("RefreshAuthRequest timed out.");
                        return false;
                    }

                    return await refreshTcs.Task;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogError("RefreshAuthRequest timed out.");
                    return false;
                }
            }
            finally
            {
                _responseSubscription?.Dispose();
                _responseSubscription = null;
                _refreshTcs = null;
                _gate.Release();
            }
        }

        private void OnRefreshAuthResponse(RefreshAuthResponse response)
        {
            if (response != null && response.success)
            {
                _logger.Log("RefreshAuthRequest succeeded.");
                _refreshTcs?.TrySetResult(true);
                return;
            }

            var message = string.IsNullOrWhiteSpace(response?.message)
                ? "The backend rejected the rotated player credential."
                : response.message;
            _logger.LogError($"RefreshAuthRequest failed: {message}");
            _refreshTcs?.TrySetResult(false);
        }

        private void OnRefreshAuthError(Exception exception)
        {
            var error = exception ?? new InvalidOperationException("RefreshAuth response stream failed.");
            _logger.LogError($"RefreshAuthRequest error: {error.Message}");
            _refreshTcs?.TrySetException(error);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _responseSubscription?.Dispose();
            _responseSubscription = null;
            _refreshTcs?.TrySetCanceled();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RefreshAuthService));
        }
    }
}
