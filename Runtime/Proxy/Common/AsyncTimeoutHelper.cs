using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Playserv.Proxy.Common
{
    internal static class AsyncTimeoutHelper
    {
        public static async Task<bool> WaitForCompletionOrTimeoutAsync(
            Task task,
            int timeoutMs,
            CancellationToken ct = default)
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
            Task timeoutTask;
            if (ct.CanBeCanceled)
                timeoutTask = Task.Delay(timeoutMs, ct);
            else
                timeoutTask = Task.Delay(timeoutMs);

            var completedTask = await Task.WhenAny(task, timeoutTask);
            if (completedTask == task)
                return true;

            if (ct.IsCancellationRequested)
                throw new OperationCanceledException(ct);

            return false;
#endif
        }
    }
}
