using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Wrapper;

namespace Playserv.GameServer
{
    public enum PlayServServerLogLevel { Debug, Info, Warning, Error }

    /// <summary>Local flush accounting; Sent never means server storage acknowledgement.</summary>
    public sealed class PlayServServerLogFlushResult
    {
        internal PlayServServerLogFlushResult(int sent, int dropped, int pending, PlayServError error)
        { Sent = sent; Dropped = dropped; Pending = pending; Error = error; }
        public int Sent { get; }
        /// <summary>Ambiguous writes discarded by this flush. Queue-overflow drops are in DroppedCount.</summary>
        public int Dropped { get; }
        public int Pending { get; }
        public PlayServError Error { get; }
        public bool IsSuccess => !Error.IsError;
    }

    /// <summary>Memory-only bounded FIFO of pre-redacted frames. Explicit opt-in, no timer or global log hook.</summary>
    public sealed class PlayServGameServerLogs
    {
        private readonly object _gate = new object();
        private State _state;
        internal PlayServGameServerLogs() { }
        public int PendingCount { get { lock (_gate) return _state?.Queue.Count ?? 0; } }
        public long PendingBytes { get { lock (_gate) return _state?.Bytes ?? 0; } }
        public long DroppedCount { get { lock (_gate) return _state?.Dropped ?? 0; } }

        /// <summary>
        /// Freezes and redacts a structured entry before retaining it. Returns false if it exceeds
        /// queue/frame bounds. Does not connect or send; serialization errors are typed and secret-safe.
        /// </summary>
        public bool TryWrite(string message, PlayServServerLogLevel level = PlayServServerLogLevel.Info, object data = null)
        {
            var context = GameServerUplinkServices.Context(default);
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (level < PlayServServerLogLevel.Debug || level > PlayServServerLogLevel.Error)
                throw new ArgumentOutOfRangeException(nameof(level));
            byte[] bytes;
            try
            {
                var frame = new { type = "log", level = LevelName(level), message, data };
                // ToPlainValue has a compatibility fallback to the original object on serialization
                // failure. Logs must fail closed instead of ever retaining an unredacted object.
                var plain = GameServerServiceValues.Codec.ParseToPlainValue(PlayServGameServerJson.Serialize(frame));
                bytes = Encoding.UTF8.GetBytes(PlayServGameServerJson.Serialize(Redact(plain, 0)));
            }
            catch
            {
                throw new PlayServGameServerException(new PlayServError(PlayServErrorCode.Serialization,
                    "log_serialization_failed", "The structured log could not be serialized."));
            }
            lock (_gate)
            {
                var state = _state;
                if (state == null || !state.Active || !ReferenceEquals(state.Context, context) || context.ShuttingDown)
                    throw UplinkErrors.Exception("server_shutting_down");
                if (bytes.Length > PlayServUplinkSocket.MaxFrameBytes || state.Queue.Count >= context.LogQueueCapacity ||
                    state.Bytes + bytes.Length > context.LogQueueMaxBytes)
                { state.Dropped++; return false; }
                state.Queue.AddLast(bytes); state.Bytes += bytes.Length; return true;
            }
        }

        /// <summary>
        /// Flushes the entries present at start; concurrent callers share one flight.
        /// Cancellation cancels only the caller's wait. The shared flush is bounded by HttpTimeout.
        /// Disconnected entries remain queued. An ambiguous send is discarded, never replayed automatically.
        /// </summary>
        public Task<PlayServServerLogFlushResult> FlushAsync(CancellationToken ct = default)
        {
            PlayServGameServer.EnsureSupportedBuildForServices(); ct.ThrowIfCancellationRequested();
            var context = PlayServGameServer.GetContextForServices();
            Task<PlayServServerLogFlushResult> task;
            lock (_gate)
            {
                var state = _state;
                if (state == null || !state.Active || !ReferenceEquals(state.Context, context))
                    throw UplinkErrors.Exception("server_shutting_down");
                if (state.Flush == null || state.Flush.IsCompleted)
                {
                    var count = state.Queue.Count;
                    state.Flush = Task.Run(() => FlushCoreAsync(state, count));
                }
                task = state.Flush;
            }
            return WaitForFlushAsync(task, ct);
        }

        private static async Task<PlayServServerLogFlushResult> WaitForFlushAsync(Task<PlayServServerLogFlushResult> task, CancellationToken ct)
        { await PlayServGameServerUplink.WaitAsync(task, ct).ConfigureAwait(false); return await task.ConfigureAwait(false); }

        private async Task<PlayServServerLogFlushResult> FlushCoreAsync(State state, int count)
        {
            var sent = 0; var dropped = 0;
            var error = PlayServError.None;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(state.Stop.Token);
            deadline.CancelAfter(state.Context.HttpTimeout);
            for (var i = 0; i < count; i++)
            {
                byte[] bytes;
                lock (_gate)
                {
                    if (!state.Active)
                    { error = new PlayServError(PlayServErrorCode.Canceled, "log_queue_reset", "The log queue was reset."); break; }
                    if (state.Queue.First == null) break;
                    bytes = state.Queue.First.Value;
                }
                if (deadline.IsCancellationRequested)
                { error = TimeoutError(); break; } // No attempted send: keep this entry.
                try
                {
                    var send = state.Uplink.SendPreparedAsync(bytes, deadline.Token);
                    _ = send.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
                    await PlayServGameServerUplink.WaitAsync(send, deadline.Token).ConfigureAwait(false);
                    CompleteEntry(state, bytes, false); sent++;
                }
                catch (PlayServGameServerException ex) when (ex.UnifiedError.SourceCode == "uplink_not_connected")
                { error = ex.UnifiedError; break; }
                catch
                {
                    // After a send attempt we cannot distinguish lost reply/partial write from no delivery.
                    CompleteEntry(state, bytes, true); dropped++;
                    error = deadline.IsCancellationRequested ? TimeoutError() :
                        UplinkErrors.Exception("uplink_send_failed", true).UnifiedError;
                    if (!state.Active) error = new PlayServError(PlayServErrorCode.Canceled, "log_queue_reset", "The log queue was reset.");
                    break;
                }
            }
            lock (_gate) return new PlayServServerLogFlushResult(sent, dropped, state.Queue.Count, error);
        }

        private void CompleteEntry(State state, byte[] bytes, bool dropped)
        {
            lock (_gate)
            {
                if (state.Queue.First != null && ReferenceEquals(state.Queue.First.Value, bytes))
                { state.Queue.RemoveFirst(); state.Bytes -= bytes.Length; if (dropped) state.Dropped++; }
            }
        }

        internal async Task<PlayServError> FlushForShutdownAsync(CancellationToken ct)
        {
            if (PendingCount == 0) return PlayServError.None;
            return (await FlushAsync(ct).ConfigureAwait(false)).Error;
        }

        internal void ResetForConfiguration(PlayServGameServerContext context = null)
        {
            lock (_gate)
            {
                var previous = _state;
                if (previous != null)
                {
                    previous.Active = false; previous.Stop.Cancel();
                    previous.Queue.Clear(); previous.Bytes = 0;
                    var pending = previous.Flush;
                    if (pending == null || pending.IsCompleted) previous.Stop.Dispose();
                    else _ = pending.ContinueWith(_ => previous.Stop.Dispose(), TaskScheduler.Default);
                }
                _state = context == null ? null : new State(context, PlayServGameServer.Uplink);
            }
        }

        private static PlayServError TimeoutError() => new PlayServError(PlayServErrorCode.Timeout,
            "request_timeout", "The structured log flush timed out.", retryable: true);
        private static string LevelName(PlayServServerLogLevel level) => level == PlayServServerLogLevel.Warning ? "warning" : level.ToString().ToLowerInvariant();
        private static readonly Regex Reservation = new Regex(@"\brsv_[A-Za-z0-9._~-]+", RegexOptions.CultureInvariant);
        internal static string Text(string value) => Reservation.Replace(GameServerServiceValues.SafeText(
            new PlayServError(PlayServErrorCode.Unknown, "", "", rawDetails: value).RawDetails) ?? "", "[REDACTED]");
        private static bool Sensitive(string key)
        {
            var normalized = Regex.Replace(key, "[^a-zA-Z0-9]", "").ToLowerInvariant();
            return normalized.Contains("token") || normalized.Contains("credential") || normalized.Contains("password") ||
                normalized.Contains("secret") || normalized == "authorization" || normalized == "serverkey" || normalized == "apikey" || normalized == "jwt";
        }
        private static object Redact(object value, int depth)
        {
            if (depth > 16) return "[REDACTED]";
            if (value is IDictionary<string, object> dictionary)
            {
                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var item in dictionary) result[Text(item.Key)] = Sensitive(item.Key) ? "[REDACTED]" : Redact(item.Value, depth + 1);
                return result;
            }
            if (value is IList list)
            {
                var result = new List<object>(list.Count);
                foreach (var item in list) result.Add(Redact(item, depth + 1));
                return result;
            }
            if (value is string text)
            {
                var trimmed = text.TrimStart();
                if (trimmed.StartsWith("{", StringComparison.Ordinal) || trimmed.StartsWith("[", StringComparison.Ordinal))
                {
                    try { return PlayServGameServerJson.Serialize(Redact(GameServerServiceValues.Codec.ParseToPlainValue(text), depth + 1)); }
                    catch { return "[REDACTED]"; }
                }
                return Text(text);
            }
            return value;
        }
        private sealed class State
        {
            internal readonly PlayServGameServerContext Context;
            internal readonly PlayServGameServerUplink Uplink;
            internal readonly LinkedList<byte[]> Queue = new LinkedList<byte[]>();
            internal readonly CancellationTokenSource Stop = new CancellationTokenSource();
            internal volatile bool Active = true;
            internal long Bytes, Dropped;
            internal Task<PlayServServerLogFlushResult> Flush;
            internal State(PlayServGameServerContext context, PlayServGameServerUplink uplink) { Context = context; Uplink = uplink; }
        }
    }
}
