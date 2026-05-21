using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Playserv.DataSubscription.Responses;
using Playserv.Modules;
using Playserv.Proxy.Common;
using ILogger = Playserv.Proxy.Logging.ILogger;

namespace Playserv.DataSubscription
{
    internal sealed class DataSubscriptionPollingCoordinator
    {
        private const int SubscriptionRequestCoalesceMs = 1000;

        private readonly IPlayServCommandBus _commandBus;
        private readonly DataGetClient _dataGetClient;
        private readonly ILogger _logger;

        public DataSubscriptionPollingCoordinator(
            IPlayServCommandBus commandBus,
            DataGetClient dataGetClient,
            ILogger logger)
        {
            _commandBus = commandBus ?? throw new ArgumentNullException(nameof(commandBus));
            _dataGetClient = dataGetClient ?? throw new ArgumentNullException(nameof(dataGetClient));
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
                null,
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
                entry,
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

            if (!TryBeginRequest(entry, SubscriptionRequestCoalesceMs))
                return;

            try
            {
                var response = await _dataGetClient.GetDataByKeyAsync(
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
            finally
            {
                EndRequest(entry);
            }
        }

        private IDisposable StartPolling(
            DataSubscriptionPollingEntry entry,
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
                entry,
                ResolveInitialDelayMs(entry),
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
            DataSubscriptionPollingEntry entry,
            int initialDelayMs,
            CancellationToken ct)
        {
            SafeLog($"[DataGet] Polling started. interval={intervalMs}ms, key={key}");
            PlayServState? pausedAtState = null;

            try
            {
                if (initialDelayMs > 0)
                    await DelayWithCancellationAsync(initialDelayMs, ct);

                while (!ct.IsCancellationRequested)
                {
                    var sdkState = _commandBus.State;
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
                    bool requestStarted = false;
                    try
                    {
                        if (entry != null && !TryBeginRequest(entry, SubscriptionRequestCoalesceMs))
                        {
                            await DelayWithCancellationAsync(intervalMs, ct);
                            continue;
                        }

                        requestStarted = entry != null;
#if PlayServ_Logs
                        SafeLog($"[DataGet] -> poll request send. key={key}, timeout={requestTimeoutMs}ms");
#endif
                        var response = await _dataGetClient.GetDataByKeyAsync(
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
                    finally
                    {
                        if (requestStarted)
                            EndRequest(entry);
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

        private static int ResolveInitialDelayMs(DataSubscriptionPollingEntry entry)
        {
            if (entry == null)
                return 0;

            return 250 + (int)(entry.SubscriptionId % 8) * 125;
        }

        private static bool TryBeginRequest(DataSubscriptionPollingEntry entry, int coalesceMs)
        {
            if (entry == null)
                return true;

            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            lock (entry)
            {
                if (entry.RequestInFlight)
                    return false;

                if (coalesceMs > 0 &&
                    entry.LastRequestCompletedAtMs > 0 &&
                    nowMs - entry.LastRequestCompletedAtMs < coalesceMs)
                {
                    return false;
                }

                entry.RequestInFlight = true;
                entry.LastRequestStartedAtMs = nowMs;
                return true;
            }
        }

        private static void EndRequest(DataSubscriptionPollingEntry entry)
        {
            if (entry == null)
                return;

            lock (entry)
            {
                entry.RequestInFlight = false;
                entry.LastRequestCompletedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }
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
