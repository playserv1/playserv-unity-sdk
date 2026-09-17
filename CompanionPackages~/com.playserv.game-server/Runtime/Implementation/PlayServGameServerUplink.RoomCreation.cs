using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Wrapper;

namespace Playserv.GameServer
{
    public sealed partial class PlayServGameServerUplink
    {
        private readonly object _creationSync = new object();
        private readonly HashSet<Task> _creations = new HashSet<Task>();
        private CancellationTokenSource _creationStop = new CancellationTokenSource();
        private bool _acceptingRoomRequests = true;

        /// <summary>Controls whether new requests are accepted. False replies instance_draining without invoking the factory.</summary>
        public bool AcceptingRoomRequests
        {
            get { lock (_creationSync) return _acceptingRoomRequests; }
            set { lock (_creationSync) _acceptingRoomRequests = value; }
        }
        /// <summary>Delivered on Unity's configured context. On failure the game must clean up any local preparation.</summary>
        public event Action<PlayServRoomCreationOutcome> RoomCreationCompleted;

        partial void HandleAdditionalFrame(string json, string type, CancellationToken ct)
        {
            if (type != "room_create") return;
            if (_context.RoomFactory == null)
            { Report(UplinkErrors.Exception("room_create_not_enabled").UnifiedError); return; }
            string name, attributesJson;
            try
            {
                var request = RoomCreateAttributes.Read<RoomCreateWire>(json);
                name = request?.room_name;
                if (name == null || !Regex.IsMatch(name, "^[A-Za-z0-9][A-Za-z0-9:._-]{0,63}$")) throw new Exception();
                attributesJson = RoomCreateAttributes.Capture(request.attributes);
            }
            catch { Report(UplinkErrors.Exception("room_create_invalid_request").UnifiedError); return; }
            var context = _context;
            var slug = _slug;
            var receivedAt = Seconds();
            lock (_creationSync)
            {
                if (_creationStop.IsCancellationRequested) return;
                // Bound dispatch work as well as the frame size. No fake ack is sent on overload.
                if (_creations.Count >= 256) return; // No unbounded queue and no invented protocol refusal.
                var stop = _creationStop.Token;
                var task = Task.Run(() => CreateRequestedRoomAsync(context, slug, name, attributesJson, receivedAt, stop, ct));
                _creations.Add(task);
                _ = task.ContinueWith(done => { lock (_creationSync) _creations.Remove(done); },
                    CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            }
        }

        private async Task CreateRequestedRoomAsync(PlayServGameServerContext context, string slug, string name,
            string attributesJson, double receivedAt, CancellationToken stop, CancellationToken connection)
        {
            var reserved = false;
            var upsertStarted = false;
            var factoryInvoked = false;
            var registrationUnknown = false;
            PlayServGameRoomHandle room = null;
            PlayServError error = PlayServError.None;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop, connection);
            var remaining = context.RoomCreateTimeout.TotalSeconds - (Seconds() - receivedAt);
            if (remaining <= 0) deadline.Cancel();
            else deadline.CancelAfter(TimeSpan.FromSeconds(remaining));
            var ct = deadline.Token;
            try
            {
                ct.ThrowIfCancellationRequested();
                string refusal = null;
                var config = RoomConfiguration;
                if (!AcceptingRoomRequests || config == null) refusal = "instance_draining";
                else reserved = PlayServGameServer.TryReserveRequestedRoom(context, slug, name, config.MaxRooms, out refusal);
                if (refusal != null)
                {
                    await SendAsync(new { type = "room_create_result", room_name = name, ok = false, reason = refusal }, ct).ConfigureAwait(false);
                    error = UplinkErrors.Exception(refusal).UnifiedError;
                    return;
                }
                var factory = context.InvokeAsync(() =>
                {
                    ct.ThrowIfCancellationRequested();
                    factoryInvoked = true;
                    Task<PlayServRoomCreateDecision> preparation;
                    try { preparation = context.RoomFactory(new PlayServRoomCreateContext(name, config, attributesJson), ct); }
                    catch (Exception ex) { throw new RoomFactoryFailure(ex); }
                    // Only faults from the game callback receive the factory wire refusal. In particular,
                    // a null task/decision, snapshot validation and registration keep their existing behavior.
                    return preparation == null ? null : ObserveRoomFactoryAsync(preparation);
                });
                // A game callback that ignores cancellation must not prevent timeout or shutdown.
                _ = factory.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
                PlayServRoomCreateDecision decision;
                try
                {
                    await WaitAsync(factory, ct).ConfigureAwait(false);
                    decision = await factory.ConfigureAwait(false);
                }
                catch (RoomFactoryFailure ex)
                {
                    // A canceled game task is a factory failure only while the SDK request is still live.
                    // Never send a late refusal on timeout, disconnect or shutdown.
                    ct.ThrowIfCancellationRequested();
                    if (Seconds() - receivedAt >= context.RoomCreateTimeout.TotalSeconds) throw new OperationCanceledException();
                    error = UplinkErrors.Exception("room_factory_failed").UnifiedError;
                    await SendAsync(new { type = "room_create_result", room_name = name, ok = false,
                        reason = "room_create_failed", detail = ex.TypeName }, ct).ConfigureAwait(false);
                    Report(error);
                    return;
                }
                ct.ThrowIfCancellationRequested();
                if (decision == null) throw UplinkErrors.Exception("room_factory_invalid_result");
                if (!decision.IsAccepted)
                {
                    await SendAsync(new { type = "room_create_result", room_name = name, ok = false,
                        reason = decision.Reason, detail = decision.Detail }, ct).ConfigureAwait(false);
                    error = UplinkErrors.Exception(decision.Reason).UnifiedError;
                    return;
                }
                PlayServGameServer.ValidateSnapshot(decision.Snapshot, nameof(decision));
                if (decision.Snapshot.RoomName != name || decision.Snapshot.Connect == null)
                    throw UplinkErrors.Exception("room_factory_invalid_result");
                if (!AcceptingRoomRequests || context.ShuttingDown)
                {
                    await SendAsync(new { type = "room_create_result", room_name = name, ok = false, reason = "instance_draining" }, ct).ConfigureAwait(false);
                    error = UplinkErrors.Exception("instance_draining").UnifiedError;
                    return;
                }
                if (Seconds() - receivedAt >= context.RoomCreateTimeout.TotalSeconds) throw new OperationCanceledException();
                await SendAsync(new { type = "room_create_result", room_name = name, ok = true }, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                upsertStarted = true;
                room = await PlayServGameServer.RegisterPreparedRoomAsync(context, slug,
                    decision.Snapshot.WithCapacity(RoomConfiguration.Capacity), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                registrationUnknown = upsertStarted;
                error = UplinkErrors.Exception(upsertStarted ? "room_registration_outcome_unknown" :
                    stop.IsCancellationRequested || connection.IsCancellationRequested ? "room_create_canceled" : "room_create_timeout").UnifiedError;
                Report(error);
            }
            catch (PlayServGameServerException ex)
            {
                registrationUnknown = upsertStarted && (ex.UnifiedError.Code == PlayServErrorCode.Network ||
                    ex.UnifiedError.Code == PlayServErrorCode.Timeout || ex.UnifiedError.SourceCode == "room_registration_outcome_unknown");
                error = upsertStarted ? ex.UnifiedError :
                    UplinkErrors.Exception(ex.UnifiedError.SourceCode == "room_factory_invalid_result" ? "room_factory_invalid_result" : "room_factory_failed").UnifiedError;
                Report(error);
            }
            catch
            {
                // Exception messages from a game callback may contain arbitrary credentials or payloads.
                error = UplinkErrors.Exception("room_factory_failed").UnifiedError; Report(error);
            }
            finally
            {
                // A timed-out write may have registered remotely. Keep the name reserved until shutdown;
                // neither a repeated frame nor a local StartRoomAsync may silently create it again.
                if (reserved && !registrationUnknown) PlayServGameServer.ReleaseRequestedRoom(context, slug, name);
                var outcome = new PlayServRoomCreationOutcome(name, room, error, factoryInvoked);
                context.Post(() => RoomCreationCompleted?.Invoke(outcome));
            }
        }

        private static async Task<PlayServRoomCreateDecision> ObserveRoomFactoryAsync(Task<PlayServRoomCreateDecision> preparation)
        {
            try { return await preparation.ConfigureAwait(false); }
            catch (Exception ex) { throw new RoomFactoryFailure(ex); }
        }

        // Do not retain the original exception (including its message, stack, inner exception or Data).
        // Wrapping here also keeps independent factory cancellation distinct from SDK cancellation in InvokeAsync.
        private sealed class RoomFactoryFailure : Exception
        {
            internal readonly string TypeName;
            internal RoomFactoryFailure(Exception exception)
            {
                TypeName = new string(exception.GetType().Name.Where(c => !char.IsControl(c)).Take(128).ToArray());
            }
        }

        internal void CancelRoomCreation()
        {
            lock (_creationSync) { _acceptingRoomRequests = false; _creationStop.Cancel(); }
        }
        internal async Task DrainRoomCreationAsync()
        {
            Task[] tasks;
            lock (_creationSync) tasks = _creations.ToArray();
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        private void ResetRoomCreation()
        {
            lock (_creationSync)
            {
                _creationStop.Dispose(); _creationStop = new CancellationTokenSource();
                _acceptingRoomRequests = true;
            }
        }
    }
}
