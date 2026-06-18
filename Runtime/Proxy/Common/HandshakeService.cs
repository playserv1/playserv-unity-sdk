using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Proxy.Interfaces;
using Playserv.Proxy.Logging;
using Playserv.Wrapper;

namespace Playserv.Proxy.Common
{
    public sealed class HandshakeService : IDisposable
    {
        private const int HandshakeTimeoutMs = 20000;

        private readonly ITransport _transport;
        private readonly ILogger _logger;
        private IDisposable _responseSubscription;
        private TaskCompletionSource<HandshakeResult> _handshakeTcs;

        public event Action<TransportError> OnError;

        public HandshakeService(ITransport transport, ILogger logger)
        {
            _transport = transport;
            _logger = logger;
        }

        public async Task<HandshakeResult> PerformHandshakeAsync(
            string handshakeCredential,
            string gameId,
            string userId,
            string gameVersion,
            string sdkVersion,
            string clientToken = null,
            string authorization = null,
            CancellationToken cancellationToken = default)
        {
            _handshakeTcs = new TaskCompletionSource<HandshakeResult>();

            _responseSubscription?.Dispose();
            _responseSubscription = _transport
                .OnReceive<HandshakeResponse>()
                .Subscribe(OnHandshakeResponse, OnHandshakeError, null);

            var request = new HandshakeRequest
            {
                GameAccessToken = handshakeCredential,
                ClientToken = clientToken,
                Authorization = authorization,
                GameId = gameId,
                UserId = userId,
                SdkVersion = sdkVersion,
                GameVersion = gameVersion
            };

            _logger.Log(
                $"Sending handshake request: SDK={sdkVersion}, Game={gameVersion}, GameId={gameId}, UserId={userId}, clientTokenSet={!string.IsNullOrWhiteSpace(clientToken)}, authorizationSet={!string.IsNullOrWhiteSpace(authorization)}");
            await _transport.Send(request);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(HandshakeTimeoutMs);

            try
            {
                var completedInTime = await WaitForCompletionOrTimeoutAsync(
                    _handshakeTcs.Task,
                    HandshakeTimeoutMs,
                    cts.Token);

                if (!completedInTime && !_handshakeTcs.Task.IsCompleted)
                {
                    _logger.LogError("Handshake timed out.");
                    return HandshakeResult.Failed(new TransportError(TransportErrorCode.None, "Handshake timed out."));
                }

                return await _handshakeTcs.Task;
            }
            catch (OperationCanceledException)
            {
                _logger.LogError("Handshake cancelled.");
                return HandshakeResult.Failed(new TransportError(TransportErrorCode.None, "Handshake cancelled."));
            }
            finally
            {
                _responseSubscription?.Dispose();
                _responseSubscription = null;
            }
        }

        private void OnHandshakeResponse(HandshakeResponse response)
        {
            if (response.success)
            {
                _logger.Log("Handshake successful.");
                _handshakeTcs?.TrySetResult(HandshakeResult.Successful());
                return;
            }

            var error = TransportError.FromCode(response.ErrorCode);
            _logger.LogError($"Handshake failed: {error}");

            HandleError(error);
            _handshakeTcs?.TrySetResult(HandshakeResult.Failed(error));
        }

        private void OnHandshakeError(Exception ex)
        {
            var error = new TransportError(TransportErrorCode.None, ex.Message);
            _logger.LogError($"Handshake error: {ex.Message}");
            _handshakeTcs?.TrySetException(ex);
        }

        private void HandleError(TransportError error)
        {
            OnError?.Invoke(error);
        }

        private static async Task<bool> WaitForCompletionOrTimeoutAsync(Task task, int timeoutMs, CancellationToken ct)
        {
            if (task.IsCompleted)
                return true;

            if (timeoutMs <= 0)
                timeoutMs = 1;

#if UNITY_WEBGL && !UNITY_EDITOR
            var stopwatch = Stopwatch.StartNew();
            while (!task.IsCompleted)
            {
                if (ct.IsCancellationRequested)
                    throw new OperationCanceledException(ct);

                if (stopwatch.ElapsedMilliseconds >= timeoutMs)
                    return false;

                await Task.Yield();
            }

            return true;
#else
            var timeoutTask = Task.Delay(timeoutMs, ct);
            var completedTask = await Task.WhenAny(task, timeoutTask);
            if (completedTask == task)
                return true;

            if (ct.IsCancellationRequested)
                throw new OperationCanceledException(ct);

            return false;
#endif
        }

        public void Dispose()
        {
            _responseSubscription?.Dispose();
            _responseSubscription = null;
        }
    }
}
