using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Playserv.DataSubscription.Responses;
using Playserv.Proxy.Common;
using ILogger = Playserv.Proxy.Logging.ILogger;

namespace Playserv.DataSubscription
{
    internal sealed class DataSubscriptionPollingCoordinator
    {
        private readonly PlayServImplementation _transport;
        private readonly DataSubscriptionRequestClient _requestClient;
        private readonly ILogger _logger;

        public DataSubscriptionPollingCoordinator(
            PlayServImplementation transport,
            DataSubscriptionRequestClient requestClient,
            ILogger logger)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _requestClient = requestClient ?? throw new ArgumentNullException(nameof(requestClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public IDisposable StartDataByKeyPolling(
            string key,
            string query,
            Dictionary<string, object> variables,
            int intervalMs,
            int requestTimeoutMs,
            Action<DataGetResponse> onData,
            Action<Exception> onError = null)
        {
            return StartPolling(
                key,
                query,
                variables,
                intervalMs,
                requestTimeoutMs,
                onData,
                onError);
        }

        public IDisposable StartSubscriptionPolling(
            DataSubscriptionPollingEntry entry,
            int intervalMs,
            int requestTimeoutMs,
            Action<DataGetResponse> onData,
            Action<Exception> onError)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));

            return StartPolling(
                entry.Key,
                entry.Query,
                entry.Variables,
                intervalMs,
                requestTimeoutMs,
                onData,
                onError);
        }

        public async Task RefreshEntryAsync(
            DataSubscriptionPollingEntry entry,
            int requestTimeoutMs,
            CancellationToken ct,
            Action<DataGetResponse> onData,
            Action<Exception> onError)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));

            try
            {
                var response = await _requestClient.GetDataByKeyAsync(
                    entry.Key,
                    entry.Query,
                    entry.Variables,
                    requestTimeoutMs,
                    ct);
                onData?.Invoke(response);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (onError != null)
                {
                    onError(ex);
                    return;
                }

                throw;
            }
        }

        private IDisposable StartPolling(
            string key,
            string query,
            Dictionary<string, object> variables,
            int intervalMs,
            int requestTimeoutMs,
            Action<DataGetResponse> onData,
            Action<Exception> onError)
        {
            if (onData == null)
                throw new ArgumentNullException(nameof(onData));

            var cts = new CancellationTokenSource();
            var variablesSnapshot = CloneVariables(variables);
            var pollingTask = PollDataByKeyLoopAsync(
                key,
                query,
                variablesSnapshot,
                intervalMs,
                requestTimeoutMs,
                onData,
                onError,
                cts.Token);

            return new PollingHandle(cts, pollingTask);
        }

        private async Task PollDataByKeyLoopAsync(
            string key,
            string query,
            Dictionary<string, object> variables,
            int intervalMs,
            int requestTimeoutMs,
            Action<DataGetResponse> onData,
            Action<Exception> onError,
            CancellationToken ct)
        {
            SafeLog($"[DataGet] Polling started. interval={intervalMs}ms, key={key}");
            PlayServState? pausedAtState = null;

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var sdkState = _transport.State;
                    if (sdkState != PlayServState.Online)
                    {
                        if (pausedAtState != sdkState)
                        {
                            SafeLogWarning($"[DataGet] poll paused. sdkState={sdkState}");
                            pausedAtState = sdkState;
                        }

                        try
                        {
                            await DelayWithCancellationAsync(intervalMs, ct);
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested)
                        {
                            break;
                        }

                        continue;
                    }

                    if (pausedAtState.HasValue)
                    {
                        SafeLog($"[DataGet] poll resumed. sdkState={sdkState}");
                        pausedAtState = null;
                    }

                    var startedAt = DateTime.UtcNow;
                    try
                    {
#if PlayServ_Logs
                        SafeLog($"[DataGet] -> poll request send. key={key}, timeout={requestTimeoutMs}ms");
#endif
                        var response = await _requestClient.GetDataByKeyAsync(
                            key,
                            query,
                            variables,
                            requestTimeoutMs,
                            ct);
#if PlayServ_Logs
                        SafeLog($"[DataGet] <- poll response received. key={key}, hasError={(response?.HasError ?? false)}");
#endif
                        onData(response);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        SafeLogError($"[DataGet] Poll request failed: {ex.Message}");
                        SafeInvokeOnError(onError, ex);
                    }

                    var elapsedMs = (int)(DateTime.UtcNow - startedAt).TotalMilliseconds;
                    var delayMs = Math.Max(0, intervalMs - elapsedMs);
                    try
                    {
                        await DelayWithCancellationAsync(delayMs, ct);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                SafeLogError($"[DataGet] Polling loop crashed: {ex.Message}");
                SafeInvokeOnError(onError, ex);
            }
            finally
            {
                SafeLog($"[DataGet] Polling stopped. key={key}");
            }
        }

        private static Dictionary<string, object> CloneVariables(Dictionary<string, object> variables)
        {
            if (variables == null || variables.Count == 0)
                return new Dictionary<string, object>();

            return new Dictionary<string, object>(variables);
        }

        private static async Task DelayWithCancellationAsync(int delayMs, CancellationToken ct)
        {
            if (delayMs <= 0)
                return;

#if UNITY_WEBGL && !UNITY_EDITOR
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            while (!ct.IsCancellationRequested)
            {
                if (stopwatch.ElapsedMilliseconds >= delayMs)
                    break;

                await Task.Yield();
            }

            if (ct.IsCancellationRequested)
                throw new OperationCanceledException(ct);
#else
            await Task.Delay(delayMs, ct);
#endif
        }

        private void SafeLog(string message)
        {
            try
            {
                _logger.Log(message);
            }
            catch
            {
            }
        }

        private void SafeLogWarning(string message)
        {
            try
            {
                _logger.LogWarning(message);
            }
            catch
            {
            }
        }

        private void SafeLogError(string message)
        {
            try
            {
                _logger.LogError(message);
            }
            catch
            {
            }
        }

        private static void SafeInvokeOnError(Action<Exception> onError, Exception ex)
        {
            if (onError == null)
                return;

            try
            {
                onError(ex);
            }
            catch
            {
            }
        }

        private sealed class PollingHandle : IDisposable
        {
            private CancellationTokenSource _cts;
            private Task _pollingTask;

            public PollingHandle(CancellationTokenSource cts, Task pollingTask)
            {
                _cts = cts;
                _pollingTask = pollingTask;
            }

            public void Dispose()
            {
                var cts = Interlocked.Exchange(ref _cts, null);
                if (cts == null)
                    return;

                if (!cts.IsCancellationRequested)
                    cts.Cancel();

                cts.Dispose();
                _pollingTask = null;
            }
        }
    }
}
