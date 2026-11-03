using System;
using System.Threading.Tasks;
using Playserv.Proxy.Common;
using Playserv.Proxy.Logging;

namespace Playserv.Proxy.Implementation
{
    internal sealed class WebRtcConnectTimeoutWatcher
    {
        private readonly int _timeoutMs;
        private readonly ILogger _logger;
        private readonly string _transportName;

        public WebRtcConnectTimeoutWatcher(int timeoutMs, ILogger logger, string transportName)
        {
            _timeoutMs = timeoutMs > 0 ? timeoutMs : 1;
            _logger = logger ?? PlayServLog.ForCategory(PlayServLogCategory.Transport);
            _transportName = string.IsNullOrWhiteSpace(transportName) ? "WebRTC transport" : transportName;
        }

        public async Task WatchAsync(
            TaskCompletionSource<bool> connectTcs,
            Func<TaskCompletionSource<bool>, bool> tryTimeout,
            Action onTimeout)
        {
            var completed = await AsyncTimeoutHelper.WaitForCompletionOrTimeoutAsync(connectTcs.Task, _timeoutMs);
            if (completed)
                return;

            if (tryTimeout == null || !tryTimeout(connectTcs))
                return;

            _logger.LogWarning($"{_transportName} connect timed out after {_timeoutMs}ms.");
            onTimeout?.Invoke();
            connectTcs.TrySetResult(false);
        }
    }
}
