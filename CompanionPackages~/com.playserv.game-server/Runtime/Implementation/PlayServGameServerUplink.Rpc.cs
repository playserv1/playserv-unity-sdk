using System;
using System.Collections;
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
        private const int RpcCallLimit = 64, RpcByteLimit = 1024 * 1024;
        private readonly object _rpcGate = new object();
        private readonly LinkedList<RpcWork> _rpcQueue = new LinkedList<RpcWork>();
        private readonly Dictionary<string, RpcWork> _rpcCalls = new Dictionary<string, RpcWork>(StringComparer.Ordinal);
        private int _rpcBytes;
        private bool _rpcRunning;
        internal int PendingRpcCalls { get { lock (_rpcGate) return _rpcCalls.Count; } }

        private sealed class RpcWork
        {
            internal PlayServServerRpcContext Caller;
            internal PlayServServerRpcRegistry.Method Method;
            internal PlayServGameServerContext Context;
            internal string Payload;
            internal int Bytes;
            internal IPlayServUplinkSocket Socket;
            internal CancellationToken Cycle;
            internal CancellationTokenSource Budget;
            internal CancellationTokenSource Deadline;
            internal CancellationToken Token;
            internal CancellationToken DeadlineToken;
            internal LinkedListNode<RpcWork> Node;
            internal bool Active, Complete;
            internal int Replied;
        }

        private void HandleRpcFrame(string json, string type, IPlayServUplinkSocket socket, CancellationToken cycle)
        {
            var context = _context;
            if (context.RpcMethods == null || context.RpcMethods.Count == 0)
            {
                if (!_rpcWarned) { _rpcWarned = true; Report(UplinkErrors.Exception("uplink_rpc_not_supported").UnifiedError); }
                return;
            }
            try
            {
                var codec = new NewtonsoftJsonCodec();
                PlayServServerRpcRegistry.RequireStrict(codec).DeserializeStrict<object>(json);
                var root = codec.ParseToPlainValue(json, new JsonCodecOptions { ParseDates = false }) as IDictionary<string, object>;
                if (root == null) throw new ArgumentException();
                if (type == "rpc_call") AcceptRpc(root, context, socket, cycle);
                else
                {
                    if (!root.TryGetValue("calls", out var values) || !(values is IList calls)) throw new ArgumentException();
                    foreach (var value in calls)
                        if (value is IDictionary<string, object> call) AcceptRpc(call, context, socket, cycle);
                        else Report(UplinkErrors.Exception("uplink_rpc_invalid_frame").UnifiedError);
                }
            }
            catch { Report(UplinkErrors.Exception("uplink_rpc_invalid_frame").UnifiedError); }
        }

        private void AcceptRpc(IDictionary<string, object> frame, PlayServGameServerContext context,
            IPlayServUplinkSocket socket, CancellationToken cycle)
        {
            PlayServServerRpcContext caller;
            string payload;
            try
            {
                caller = new PlayServServerRpcContext(frame);
                if (string.IsNullOrEmpty(caller.CallId) || caller.CallId.Length > 256 || caller.CallId.Any(char.IsControl)) throw new ArgumentException();
                payload = PlayServServerRpcContext.Text(frame, "payload_base64");
            }
            catch { Report(UplinkErrors.Exception("uplink_rpc_invalid_frame").UnifiedError); return; }
            var work = new RpcWork { Caller = caller, Context = context, Payload = payload, Socket = socket, Cycle = cycle,
                Bytes = Encoding.UTF8.GetByteCount(PlayServGameServerJson.Serialize(frame)) };
            string refusal = null;
            lock (_rpcGate)
            {
                if (cycle.IsCancellationRequested || _rpcCalls.ContainsKey(caller.CallId)) return;
                if (caller.MethodName == null || !context.RpcMethods.TryGetValue(caller.MethodName, out work.Method)) refusal = "rpc_method_not_found";
                else if (_rpcCalls.Count >= RpcCallLimit || work.Bytes > RpcByteLimit - _rpcBytes) refusal = "rpc_queue_full";
                else
                {
                    work.Budget = new CancellationTokenSource();
                    work.Deadline = CancellationTokenSource.CreateLinkedTokenSource(cycle);
                    work.Token = work.Budget.Token;
                    work.DeadlineToken = work.Deadline.Token;
                    _rpcCalls.Add(caller.CallId, work); _rpcBytes += work.Bytes;
                    work.Node = _rpcQueue.AddLast(work);
                    if (!_rpcRunning) { _rpcRunning = true; _ = Task.Run(RunRpcQueueAsync); }
                }
            }
            if (refusal != null) { _ = ReplyRpcAsync(work, refusal, null); return; }
            _ = WatchRpcDeadlineAsync(work);
        }

        private async Task WatchRpcDeadlineAsync(RpcWork work)
        {
            var expired = false;
            try { await Task.Delay(work.Context.HttpTimeout, work.DeadlineToken).ConfigureAwait(false); expired = true; }
            catch (OperationCanceledException) { }
            bool finished;
            lock (_rpcGate)
            {
                if (work.Complete) return;
                finished = !work.Active;
                if (finished) FinishRpcLocked(work);
            }
            var reply = expired ? ReplyRpcAsync(work, "rpc_timeout", null) : Task.CompletedTask;
            CancelRpcBudget(work, finished);
            await reply.ConfigureAwait(false);
        }

        private async Task RunRpcQueueAsync()
        {
            while (true)
            {
                RpcWork work;
                lock (_rpcGate)
                {
                    if (_rpcQueue.Count == 0) { _rpcRunning = false; return; }
                    work = _rpcQueue.First.Value; _rpcQueue.RemoveFirst(); work.Node = null; work.Active = true;
                }
                try
                {
                    work.Cycle.ThrowIfCancellationRequested();
                    work.Token.ThrowIfCancellationRequested();
                    PlayServServerRpcArguments args;
                    try { args = work.Method.Bind(work.Payload); }
                    catch { await ReplyRpcAsync(work, "rpc_invalid_arguments", null).ConfigureAwait(false); continue; }
                    var result = await work.Context.InvokeAsync(() =>
                    {
                        work.Cycle.ThrowIfCancellationRequested();
                        work.Token.ThrowIfCancellationRequested();
                        return work.Method.Invoke(work.Caller, args, work.Token);
                    }).ConfigureAwait(false);
                    if (!work.Token.IsCancellationRequested) await ReplyRpcAsync(work, null, result).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (!work.Cycle.IsCancellationRequested && !work.Token.IsCancellationRequested)
                        await ReplyRpcAsync(work, "rpc_handler_failed", null).ConfigureAwait(false);
                }
                catch { await ReplyRpcAsync(work, "rpc_handler_failed", null).ConfigureAwait(false); }
                finally
                {
                    lock (_rpcGate) FinishRpcLocked(work);
                    CancelRpcBudget(work, true);
                }
                // A handler ignoring cancellation continues to occupy the serial worker and its memory budget.
                // It must finish before the next handler starts; no overlapping 'retry' of game side effects.
            }
        }

        private void FinishRpcLocked(RpcWork work)
        {
            if (work.Complete) return;
            work.Complete = true;
            if (work.Node != null) _rpcQueue.Remove(work.Node);
            _rpcCalls.Remove(work.Caller.CallId); _rpcBytes -= work.Bytes;
            work.Payload = null;
        }

        private static void CancelRpcBudget(RpcWork work, bool dispose)
        {
            // Game cancellation registrations must never execute while holding SDK locks.
            try { work.Budget.Cancel(); } catch { }
            if (dispose)
            {
                try { work.Deadline.Cancel(); } catch { }
                work.Budget.Dispose(); work.Deadline.Dispose();
            }
        }

        private async Task ReplyRpcAsync(RpcWork work, string error, object result)
        {
            if (work.Caller.OneWay || work.Cycle.IsCancellationRequested || Interlocked.Exchange(ref work.Replied, 1) != 0) return;
            try
            {
                byte[] bytes;
                try
                {
                    bytes = Encoding.UTF8.GetBytes(PlayServGameServerJson.Serialize(new
                    { type = "rpc_result", id = work.Caller.CallId, status = error == null ? "ok" : "error", message = error, result }));
                    if (bytes.Length > PlayServUplinkSocket.MaxFrameBytes) throw new ArgumentException();
                }
                catch
                {
                    bytes = Encoding.UTF8.GetBytes(PlayServGameServerJson.Serialize(new
                    { type = "rpc_result", id = work.Caller.CallId, status = "error", message = "rpc_result_invalid" }));
                }
                lock (_sync) if (!ReferenceEquals(_socket, work.Socket) || _state != PlayServUplinkState.Connected) return;
                await work.Socket.SendAsync(bytes, work.Cycle).ConfigureAwait(false);
            }
            catch { if (!work.Cycle.IsCancellationRequested) work.Socket.Abort(); }
        }
    }
}
