using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Wrapper;

namespace Playserv.GameServer
{
    /// <summary>An immutable entry in the backend's project-level best-score ranking.</summary>
    public sealed class PlayServServerLeaderboardEntry
    {
        internal PlayServServerLeaderboardEntry(int rank, string playerId, string displayName, long score, DateTimeOffset recordedAt)
        { Rank = rank; PlayerId = playerId; DisplayName = displayName; Score = score; RecordedAt = recordedAt; }
        public int Rank { get; }
        public string PlayerId { get; }
        public string DisplayName { get; }
        public long Score { get; }
        public DateTimeOffset RecordedAt { get; }
    }

    /// <summary>Top entries in backend order, plus an optional viewer outside Top. Not a cursor page or AroundMe window.</summary>
    public sealed class PlayServServerLeaderboardResult
    {
        internal PlayServServerLeaderboardResult(List<PlayServServerLeaderboardEntry> entries) => Entries = entries.AsReadOnly();
        public IReadOnlyList<PlayServServerLeaderboardEntry> Entries { get; }
    }

    /// <summary>Existing project-level score store; no separate boards, seasons or environment-isolated ranking.</summary>
    public sealed class PlayServGameServerLeaderboards
    {
        internal PlayServGameServerLeaderboards() { }

        /// <summary>
        /// Sends score_submit. Completion confirms socket send only, not storage. Optional caller-owned
        /// idempotencyKey is transmitted verbatim; the SDK never retries or persists a submission.
        /// </summary>
        public Task SubmitScoreAsync(string playerId, long score, object metadata = null, string idempotencyKey = null,
            CancellationToken ct = default)
        {
            GameServerUplinkServices.Context(ct);
            GameServerServiceValues.RequireText(playerId, nameof(playerId));
            if (idempotencyKey != null) GameServerServiceValues.RequireText(idempotencyKey, nameof(idempotencyKey));
            return PlayServGameServer.Uplink.SendPreparedAsync(GameServerUplinkServices.Frame(
                new { type = "score_submit", player_id = playerId, score, metadata, idempotency_key = idempotencyKey }), ct);
        }

        /// <summary>
        /// Queries Top 1–100 (default 10). An optional viewer may be appended outside Top.
        /// Uses HttpTimeout for its response deadline; does not connect, retry or fall back to REST.
        /// </summary>
        public Task<PlayServServerLeaderboardResult> GetTopAsync(int top = 10, string viewerPlayerId = null, CancellationToken ct = default)
        {
            var context = GameServerUplinkServices.Context(ct);
            if (top < 1 || top > 100) throw new ArgumentOutOfRangeException(nameof(top));
            if (viewerPlayerId != null) GameServerServiceValues.RequireText(viewerPlayerId, nameof(viewerPlayerId));
            var id = Guid.NewGuid().ToString("N");
            var frame = GameServerUplinkServices.Frame(new { type = "leaderboard_query", request_id = id, top, viewer_player_id = viewerPlayerId });
            return PlayServGameServer.Uplink.QueryLeaderboardAsync(context, id, frame, ct);
        }

        internal static PlayServServerLeaderboardResult Parse(IDictionary<string, object> body)
        {
            if (!body.TryGetValue("entries", out var value) || !(value is IList entries) || entries.Count > 101)
                throw InvalidResult();
            var result = new List<PlayServServerLeaderboardEntry>(entries.Count);
            foreach (var item in entries)
            {
                if (!(item is IDictionary<string, object> row) ||
                    !row.TryGetValue("rank", out var rankValue) || !Integer(rankValue, out var rank) || rank < 1 || rank > int.MaxValue ||
                    !row.TryGetValue("score", out var scoreValue) || !Integer(scoreValue, out var score) ||
                    !row.TryGetValue("player_id", out var playerValue) || !(playerValue is string player) || string.IsNullOrWhiteSpace(player) ||
                    (row.TryGetValue("display_name", out var name) && name != null && !(name is string)) ||
                    !row.TryGetValue("recorded_at", out var time) || !Timestamp(time, out var stamp))
                    throw InvalidResult();
                result.Add(new PlayServServerLeaderboardEntry((int)rank, player, name as string, score, stamp));
            }
            return new PlayServServerLeaderboardResult(result);
        }
        private static bool Integer(object value, out long result)
        {
            if (value is long l) { result = l; return true; }
            if (value is int i) { result = i; return true; }
            result = 0; return false;
        }
        private static bool Timestamp(object value, out DateTimeOffset result)
        {
            if (value is DateTime date)
            { result = new DateTimeOffset(date.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(date, DateTimeKind.Utc) : date); return true; }
            result = default;
            return value is string text && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result);
        }
        internal static PlayServGameServerException InvalidResult() => new PlayServGameServerException(
            new PlayServError(PlayServErrorCode.InvalidResponse, "invalid_response", "The leaderboard response is invalid."));
    }

    public sealed partial class PlayServGameServerUplink
    {
        private readonly Dictionary<string, TaskCompletionSource<PlayServServerLeaderboardResult>> _serviceQueries =
            new Dictionary<string, TaskCompletionSource<PlayServServerLeaderboardResult>>(StringComparer.Ordinal);
        internal int PendingServiceQueryCount { get { lock (_sync) return _serviceQueries.Count; } }

        internal async Task<PlayServServerLeaderboardResult> QueryLeaderboardAsync(
            PlayServGameServerContext context, string id, byte[] bytes, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var completion = new TaskCompletionSource<PlayServServerLeaderboardResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            // Disconnect may finish a request while SendAsync is still in flight.
            _ = completion.Task.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            IPlayServUplinkSocket socket;
            lock (_sync)
            {
                if (_state != PlayServUplinkState.Connected || !ReferenceEquals(_context, context))
                    throw UplinkErrors.Exception("uplink_not_connected", true);
                if (_serviceQueries.Count >= 128) throw UplinkErrors.Exception("uplink_pending_limit");
                socket = _socket;
                _serviceQueries.Add(id, completion);
            }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(context.HttpTimeout);
            try
            {
                var send = socket.SendAsync(bytes, timeout.Token);
                _ = send.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
                await WaitAsync(Task.WhenAny(send, completion.Task), timeout.Token).ConfigureAwait(false);
                if (completion.Task.IsCompleted) return await completion.Task.ConfigureAwait(false);
                await send.ConfigureAwait(false);
                await WaitAsync(completion.Task, timeout.Token).ConfigureAwait(false);
                return await completion.Task.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                throw new PlayServGameServerException(new PlayServError(PlayServErrorCode.Timeout,
                    "request_timeout", "The leaderboard query timed out.", retryable: true));
            }
            catch (PlayServGameServerException) { throw; }
            catch { socket.Abort(); throw UplinkErrors.Exception("uplink_send_failed", true); }
            finally { lock (_sync) _serviceQueries.Remove(id); }
        }

        private void HandleServiceReply(string json, bool oversized)
        {
            var body = GameServerServiceValues.Object(json);
            if (body == null || !body.TryGetValue("request_id", out var value) || !(value is string id)) return;
            TaskCompletionSource<PlayServServerLeaderboardResult> completion;
            lock (_sync)
            {
                if (!_serviceQueries.TryGetValue(id, out completion)) return; // Unknown or late response.
                _serviceQueries.Remove(id);
            }
            try
            {
                if (oversized) throw UplinkErrors.Exception("frame_too_large");
                completion.TrySetResult(PlayServGameServerLeaderboards.Parse(body));
            }
            catch { completion.TrySetException(oversized ? UplinkErrors.Exception("frame_too_large") : PlayServGameServerLeaderboards.InvalidResult()); }
        }

        private void FailServiceQueries(PlayServGameServerException error)
        {
            lock (_sync)
            {
                foreach (var query in _serviceQueries.Values) query.TrySetException(error);
                _serviceQueries.Clear();
            }
        }
    }
}
