using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Wrapper;

namespace Playserv.Matchmaking
{
    /// <summary>Explicit budgets for named-room join; no effect on existing low-level calls.</summary>
    public sealed class PlayServRoomJoinOptions
    {
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(45);
        public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(30);
        public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);
        public int MaxUnreachableRetries { get; set; } = 3;
    }

    internal sealed partial class PlayServMatchmakingClient
    {
        private TimeSpan? ReadRetryAfter(IReadOnlyDictionary<string, string> headers)
        {
            if (headers == null || !headers.TryGetValue("Retry-After", out var value)) return null;
            if (long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) && seconds <= int.MaxValue)
                return TimeSpan.FromSeconds(seconds);
            if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var retry)) return null;
            var now = _utcNow();
            if (headers.TryGetValue("Date", out var date) && DateTimeOffset.TryParse(date, out var serverNow)) now = serverNow;
            return retry > now ? retry - now : TimeSpan.Zero;
        }

        internal async Task<PlayServMatchReservation> JoinRoomAndConnectAsync(PlayServJoinRoomRequest request,
            PlayServRoomJoinOptions options, Func<PlayServMatchReservation, CancellationToken, Task> connector, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (request == null) throw new ArgumentNullException(nameof(request));
            var delivery = SynchronizationContext.Current;
            if (connector != null && delivery == null) throw new InvalidOperationException("Call with a connector from Unity's synchronization context.");
            options = options ?? new PlayServRoomJoinOptions();
            var timeout = options.Timeout; var connectTimeout = options.ConnectTimeout;
            var poll = options.PollInterval; var retries = options.MaxUnreachableRetries;
            foreach (var duration in new[] { timeout, connectTimeout, poll })
                if (duration <= TimeSpan.Zero || duration.TotalMilliseconds > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(options));
            if (retries < 0) throw new ArgumentOutOfRangeException(nameof(options));
            var snapshot = new PlayServJoinRoomRequest { FunctionSlug = request.FunctionSlug, RoomName = request.RoomName,
                Params = SnapshotParameters(request.Params, nameof(request)) };
            var start = _monotonicSeconds(); double? connectStarted = null;
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budget.CancelAfter(timeout);
            try
            {
                while (true)
                {
                    budget.Token.ThrowIfCancellationRequested();
                    var remaining = timeout.TotalSeconds - (_monotonicSeconds() - start);
                    if (remaining <= 0) throw JoinBudgetExpired(snapshot.FunctionSlug);
                    PlayServMatchReservation reservation = null;
                    var wait = poll;
                    try
                    {
                        var result = await AwaitJoinBudget(JoinRoomAsync(snapshot, budget.Token), budget.Token);
                        reservation = result.Reservation;
                    }
                    catch (PlayServMatchmakingException ex) when (ex.RoomFailureCode == PlayServRoomFailureCode.RoomUnreachable && retries > 0)
                    { retries--; wait = ex.RetryAfter ?? poll; }
                    if (_monotonicSeconds() - start >= timeout.TotalSeconds) throw JoinBudgetExpired(snapshot.FunctionSlug);
                    if (reservation != null)
                    {
                        var connect = reservation.Connect;
                        var ready = connect != null && (!string.IsNullOrWhiteSpace(connect.ConnectString) ||
                            !string.IsNullOrWhiteSpace(connect.Host) && connect.Port > 0);
                        if (ready && reservation.RemainingLifetime > TimeSpan.Zero)
                        {
                            if (connector == null) return reservation;
                            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                            delivery.Post(async _ =>
                            {
                                try
                                {
                                    budget.Token.ThrowIfCancellationRequested();
                                    if (_monotonicSeconds() - start >= timeout.TotalSeconds) throw JoinBudgetExpired(snapshot.FunctionSlug);
                                    if (reservation.RemainingLifetime <= TimeSpan.Zero) { completion.TrySetResult(false); return; }
                                    await connector(reservation, budget.Token);
                                    completion.TrySetResult(true);
                                }
                                catch (OperationCanceledException) { completion.TrySetCanceled(); }
                                catch (Exception ex) { completion.TrySetException(ex); }
                            }, null);
                            if (await AwaitJoinBudget(completion.Task, budget.Token)) return reservation;
                        }
                        if (!ready)
                        {
                            connectStarted = connectStarted ?? _monotonicSeconds();
                            var connectRemaining = connectTimeout.TotalSeconds - (_monotonicSeconds() - connectStarted.Value);
                            if (connectRemaining <= 0) throw JoinBudgetExpired(snapshot.FunctionSlug);
                            wait = TimeSpan.FromSeconds(Math.Min(wait.TotalSeconds, connectRemaining));
                        }
                    }
                    remaining = timeout.TotalSeconds - (_monotonicSeconds() - start);
                    if (remaining <= 0) throw JoinBudgetExpired(snapshot.FunctionSlug);
                    await AwaitJoinBudget(_delay((int)Math.Ceiling(Math.Min(remaining * 1000, Math.Max(1, wait.TotalMilliseconds))), budget.Token), budget.Token);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw new OperationCanceledException(ct); }
            catch (OperationCanceledException) when (budget.IsCancellationRequested) { throw JoinBudgetExpired(snapshot.FunctionSlug); }
        }

        private static PlayServMatchmakingException JoinBudgetExpired(string slug) => new PlayServMatchmakingException(
            PlayServMatchmakingOperation.JoinRoom, slug, new PlayServError(PlayServErrorCode.Timeout,
                "room_join_timeout", "The room join budget expired."));

        private static async Task AwaitJoinBudget(Task task, CancellationToken ct)
        {
            var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (ct.Register(() => canceled.TrySetCanceled()))
            {
                var completed = await Task.WhenAny(task, canceled.Task);
                if (completed != task)
                    _ = task.ContinueWith(t => { var ignored = t.Exception; }, CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                await completed;
            }
        }
        private static async Task<T> AwaitJoinBudget<T>(Task<T> task, CancellationToken ct)
        { await AwaitJoinBudget((Task)task, ct); return await task; }
    }
}
