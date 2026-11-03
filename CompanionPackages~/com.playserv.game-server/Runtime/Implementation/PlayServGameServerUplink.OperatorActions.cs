using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Serialization;

namespace Playserv.GameServer
{
    public sealed partial class PlayServGameServerUplink
    {
        private const int OperatorLimit = 64, OperatorBytesLimit = 1024 * 1024;
        private readonly object _operatorGate = new object();
        private readonly LinkedList<OperatorWork> _operatorQueue = new LinkedList<OperatorWork>();
        private readonly Dictionary<string, OperatorWork> _operatorCalls = new Dictionary<string, OperatorWork>(StringComparer.Ordinal);
        private bool _operatorRunning;
        private int _operatorBytes;
        internal TimeSpan OperatorTimeout = TimeSpan.FromSeconds(5);
        internal int PendingOperatorActions { get { lock (_operatorGate) return _operatorCalls.Count; } }

        private sealed class OperatorWork
        {
            internal string Type, RequestId, RoomName, PlayerId;
            internal int Bytes;
            internal double Until;
            internal PlayServGameServerContext Context;
            internal IPlayServUplinkSocket Socket;
            internal CancellationTokenSource Budget;
            internal CancellationToken Token;
            internal LinkedListNode<OperatorWork> Node;
            internal bool Active, Complete;
            internal RoomGeneration Generation;
            internal Task<bool> Reporting;
        }

        private void HandleOperatorAction(string json, string type, IPlayServUplinkSocket socket, CancellationToken cycle, double receivedAt)
        {
            var context = _context;
            if (type == "participant_remove" ? context.ParticipantRemoveHandler == null : context.RoomCloseHandler == null) return;
            OperatorWork work;
            try
            {
                var codec = new NewtonsoftJsonCodec();
                PlayServServerRpcRegistry.RequireStrict(codec).DeserializeStrict<object>(json);
                var fields = codec.ParseToPlainValue(json, new JsonCodecOptions { ParseDates = false }) as IDictionary<string, object>;
                if (fields == null) throw new ArgumentException();
                var requestId = Field(fields, "request_id", "^[^\\s\\p{Cc}]{1,256}\\z");
                work = new OperatorWork { Type = type, RequestId = requestId, Context = context, Socket = socket,
                    RoomName = Field(fields, "room_name", "^[A-Za-z0-9][A-Za-z0-9:._-]{0,63}\\z"),
                    PlayerId = type == "participant_remove" ? Field(fields, "player_id", "^plr_[A-Za-z0-9_-]{1,128}\\z") : null,
                    Bytes = Encoding.UTF8.GetByteCount(json), Until = receivedAt + OperatorTimeout.TotalSeconds };
            }
            catch { Report(UplinkErrors.Exception("operator_action_invalid_frame").UnifiedError); return; }
            var overflow = false;
            lock (_operatorGate)
            {
                if (cycle.IsCancellationRequested || _operatorCalls.ContainsKey(work.RequestId)) return;
                if (_operatorCalls.Count >= OperatorLimit || work.Bytes > OperatorBytesLimit - _operatorBytes)
                    overflow = true;
                else
                {
                    work.Budget = CancellationTokenSource.CreateLinkedTokenSource(cycle);
                    work.Token = work.Budget.Token;
                    _operatorCalls.Add(work.RequestId, work); _operatorBytes += work.Bytes;
                }
            }
            if (overflow) { Report(UplinkErrors.Exception("operator_action_queue_full").UnifiedError); return; }
            var remaining = OperatorTimeout.TotalSeconds - (Seconds() - receivedAt);
            if (remaining <= 0) work.Budget.Cancel();
            else work.Budget.CancelAfter(TimeSpan.FromSeconds(remaining));
            work.Generation = PlayServGameServer.BeginOperatorAction(context, _slug, work.RoomName, work.PlayerId,
                frame => SendOperatorPresenceAsync(work, frame), out work.Reporting);
            lock (_operatorGate)
            {
                work.Node = _operatorQueue.AddLast(work);
                if (!_operatorRunning) { _operatorRunning = true; _ = Task.Run(RunOperatorQueueAsync); }
            }
            _ = WatchOperatorAsync(work);
        }

        private async Task WatchOperatorAsync(OperatorWork work)
        {
            try { await Task.Delay(Timeout.Infinite, work.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            lock (_operatorGate)
            {
                // An active handler that ignores cancellation keeps its lane and memory budget until it finishes.
                if (!work.Complete && !work.Active)
                { FinishOperatorLocked(work); work.Budget.Dispose(); }
            }
        }

        private async Task RunOperatorQueueAsync()
        {
            while (true)
            {
                OperatorWork work;
                lock (_operatorGate)
                {
                    if (_operatorQueue.Count == 0) { _operatorRunning = false; return; }
                    work = _operatorQueue.First.Value; _operatorQueue.RemoveFirst(); work.Node = null; work.Active = true;
                }
                try
                {
                    work.Token.ThrowIfCancellationRequested();
                    var ok = await work.Context.InvokeAsync(() =>
                    {
                        work.Token.ThrowIfCancellationRequested();
                        if (Seconds() >= work.Until) throw new OperationCanceledException();
                        return work.Type == "participant_remove"
                            ? work.Context.ParticipantRemoveHandler(new PlayServParticipantRemoveContext(work.RequestId, work.RoomName, work.PlayerId), work.Token)
                            : work.Context.RoomCloseHandler(new PlayServOperatorRoomCloseContext(work.RequestId, work.RoomName), work.Token);
                    }).ConfigureAwait(false);
                    await WaitAsync(work.Reporting, work.Token).ConfigureAwait(false);
                    await ReplyOperatorAsync(work, ok && await work.Reporting.ConfigureAwait(false)).ConfigureAwait(false);
                }
                catch { await ReplyOperatorAsync(work, false).ConfigureAwait(false); }
                finally
                {
                    lock (_operatorGate) FinishOperatorLocked(work);
                    try { work.Budget.Cancel(); } catch { }
                    work.Budget.Dispose();
                }
            }
        }

        private void FinishOperatorLocked(OperatorWork work)
        {
            if (work.Complete) return;
            work.Complete = true;
            if (work.Node != null) _operatorQueue.Remove(work.Node);
            _operatorCalls.Remove(work.RequestId); _operatorBytes -= work.Bytes;
            PlayServGameServer.EndOperatorAction(work.Generation);
        }

        internal void CancelOperatorActions()
        {
            OperatorWork[] pending;
            lock (_operatorGate) pending = _operatorCalls.Values.ToArray();
            foreach (var work in pending) { try { work.Budget.Cancel(); } catch { } }
        }

        private async Task<bool> SendOperatorPresenceAsync(OperatorWork work, object frame)
        {
            if (work.Token.IsCancellationRequested || work.Context.ShuttingDown || Seconds() >= work.Until) return false;
            lock (_sync)
                if (!ReferenceEquals(_context, work.Context) || !ReferenceEquals(_socket, work.Socket) || _state != PlayServUplinkState.Connected) return false;
            try
            {
                await work.Socket.SendAsync(Encoding.UTF8.GetBytes(PlayServGameServerJson.Serialize(frame)), work.Token).ConfigureAwait(false);
                return true;
            }
            catch { return false; }
        }

        private async Task ReplyOperatorAsync(OperatorWork work, bool ok)
        {
            if (work.Token.IsCancellationRequested || work.Context.ShuttingDown || Seconds() >= work.Until) return;
            lock (_sync)
                if (!ReferenceEquals(_context, work.Context) || !ReferenceEquals(_socket, work.Socket) || _state != PlayServUplinkState.Connected) return;
            var frame = ok ? (object)new { type = work.Type + "_result", request_id = work.RequestId, ok = true } :
                new { type = work.Type + "_result", request_id = work.RequestId, ok = false, reason = "operator_action_failed" };
            try { await work.Socket.SendAsync(Encoding.UTF8.GetBytes(PlayServGameServerJson.Serialize(frame)), work.Token).ConfigureAwait(false); }
            catch { if (!work.Token.IsCancellationRequested) work.Socket.Abort(); }
        }
    }
}
