using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Playserv.Proxy.Common
{
    internal sealed class AsyncOperationBudget : IDisposable
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly TimeSpan _timeout;
        private readonly CancellationTokenSource _source;
        internal AsyncOperationBudget(TimeSpan timeout, CancellationToken ct)
        {
            if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(timeout));
            _timeout = timeout;
            _source = CancellationTokenSource.CreateLinkedTokenSource(ct);
            Token = _source.Token;
        }
        internal CancellationToken Token { get; }
        internal TimeSpan Remaining => _timeout > _clock.Elapsed ? _timeout - _clock.Elapsed : TimeSpan.Zero;
        internal void Check()
        {
            Token.ThrowIfCancellationRequested();
            if (Remaining <= TimeSpan.Zero) throw new TimeoutException();
        }
        internal async Task<T> RunAsync<T>(Task<T> task, Func<T, Task> onLateResult = null)
        {
            try
            {
                Check();
                if (!await AsyncTimeoutHelper.WaitForCompletionOrTimeoutAsync(task, (int)Math.Ceiling(Remaining.TotalMilliseconds), Token))
                    throw new TimeoutException();
                Token.ThrowIfCancellationRequested();
                return await task;
            }
            catch
            {
                _ = ObserveLateAsync(task, onLateResult);
                throw;
            }
        }
        internal async Task RunAsync(Task task) => await RunAsync(AsResult(task));
        private static async Task<bool> AsResult(Task task) { await task; return true; }
        private static async Task ObserveLateAsync<T>(Task<T> task, Func<T, Task> cleanup)
        {
            try { var result = await task; if (cleanup != null) await cleanup(result); }
            catch { }
        }
        internal Task DelayAsync(TimeSpan delay) => RunAsync(Delay(delay, Token));
        internal static async Task Delay(TimeSpan delay, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (delay <= TimeSpan.Zero) return;
            var never = new TaskCompletionSource<bool>();
            await AsyncTimeoutHelper.WaitForCompletionOrTimeoutAsync(never.Task,
                (int)Math.Min(int.MaxValue, Math.Ceiling(delay.TotalMilliseconds)), ct);
        }
        internal static TimeSpan RetryDelay(IReadOnlyDictionary<string,string> headers, TimeSpan fallback)
        {
            if (headers == null) return fallback;
            foreach (var pair in headers)
            {
                if (!string.Equals(pair.Key, "Retry-After", StringComparison.OrdinalIgnoreCase)) continue;
                TimeSpan delay;
                if (double.TryParse(pair.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) && seconds >= 0 && seconds <= int.MaxValue / 1000)
                    delay = TimeSpan.FromSeconds(seconds);
                else if (DateTimeOffset.TryParse(pair.Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
                    delay = date - DateTimeOffset.UtcNow;
                else return fallback;
                return delay > fallback ? delay : fallback;
            }
            return fallback;
        }
        public void Dispose()
        {
            try { _source.Cancel(); } catch (AggregateException) { }
            _source.Dispose();
        }
    }
}
