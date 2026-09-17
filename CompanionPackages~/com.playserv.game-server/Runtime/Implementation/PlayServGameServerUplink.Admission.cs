using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Wrapper;

namespace Playserv.GameServer
{
    public sealed partial class PlayServGameServerUplink
    {
        // Lock order: room -> admission. Never call a room or a user callback under this lock.
        private readonly object _admissionSync = new object();
        private readonly Dictionary<string, Ticket> _tickets = new Dictionary<string, Ticket>(StringComparer.Ordinal);
        private readonly Dictionary<string, PlayServGameRoomHandle> _admissionRooms = new Dictionary<string, PlayServGameRoomHandle>(StringComparer.Ordinal);
        private long _ticketBytes;
        private PlayServAdmissionMode _admissionMode;
        private IPlayServUplinkSocket _admissionSocket;
        private CancellationToken _admissionCancellation;
        internal TimeSpan OfferTimeout = TimeSpan.FromSeconds(3);
        /// <summary>The negotiated path. ConsumeReservationAsync must not be used when this is Push.</summary>
        public PlayServAdmissionMode AdmissionMode { get { lock (_admissionSync) return _admissionMode; } }
        internal int RetainedAdmissionCount { get { lock (_admissionSync) return _tickets.Count; } }

        private sealed class Ticket
        {
            internal PlayServTicketOffer Offer;
            internal string Signature, AdmissionId;
            internal int Bytes;
            internal double Deadline, Expires;
            internal bool Available, Spent, Pending;
            internal PlayServTicketDecision Decision;
            internal PlayServGameRoomHandle Room;
            internal PlayServGameServerContext Context;
            internal readonly TaskCompletionSource<PlayServTicketDecision> Completion = NewTicketCompletion();
            private static TaskCompletionSource<PlayServTicketDecision> NewTicketCompletion() =>
                new TaskCompletionSource<PlayServTicketDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private void SetAdmissionConnection(string mode, IPlayServUplinkSocket socket, CancellationToken ct)
        {
            if (mode != null && mode != "consume" && mode != "push" || mode == "push" && !_context.EnablePushedAdmission)
                throw new PlayServUplinkFailure("uplink_invalid_hello_ack", true);
            lock (_admissionSync)
            {
                _admissionMode = mode == "push" ? PlayServAdmissionMode.Push : PlayServAdmissionMode.Consume;
                _admissionSocket = socket; _admissionCancellation = ct;
                if (_admissionMode != PlayServAdmissionMode.Push) { _tickets.Clear(); _ticketBytes = 0; }
            }
        }
        internal void PrepareAdmissionRoom(string name)
        {
            lock (_admissionSync) { if (!_admissionRooms.ContainsKey(name)) _admissionRooms.Add(name, null); }
        }
        internal void AttachAdmissionRoom(string name, PlayServGameRoomHandle room)
        { lock (_admissionSync) _admissionRooms[name] = room; }
        internal void ClearAdmissionRoom(string name)
        {
            lock (_admissionSync)
            {
                _admissionRooms.Remove(name);
                foreach (var entry in _tickets.Values.Where(e => e.Offer.RoomName == name).ToArray()) RemoveTicket(entry);
            }
        }
        private void RemoveTicket(Ticket ticket)
        {
            if (!_tickets.TryGetValue(ticket.Offer.ReservationToken, out var current) || !ReferenceEquals(ticket, current)) return;
            _tickets.Remove(ticket.Offer.ReservationToken); _ticketBytes -= ticket.Bytes;
            ticket.Available = false; ticket.Completion.TrySetResult(null);
        }
        private void LoseAdmissionConnection(bool clear = false)
        {
            Ticket[] pending;
            lock (_admissionSync)
            {
                _admissionSocket = null;
                pending = _tickets.Values.Where(e => e.Pending).ToArray();
                foreach (var entry in pending) entry.Pending = false;
                foreach (var entry in _tickets.Values.Where(e => e.Decision == null).ToArray()) RemoveTicket(entry);
                if (clear) { _tickets.Clear(); _ticketBytes = 0; _admissionMode = PlayServAdmissionMode.Unknown; }
            }
            foreach (var entry in pending) RejectAdmission(entry, "admission_connection_lost");
        }

        private void HandleAdmissionFrame(string json, string type, CancellationToken ct)
        {
            try
            {
                if (AdmissionMode != PlayServAdmissionMode.Push) throw new ArgumentException();
                var fields = PlayServGameServerJson.ParseOptionalObject(json) as IDictionary<string, object>;
                if (fields == null) throw new ArgumentException();
                var token = Field(fields, "reservation_token", "^rsv_[A-Za-z0-9_-]{1,1024}$");
                var room = Field(fields, "room_name", "^[A-Za-z0-9][A-Za-z0-9:._-]{0,63}$");
                var player = Field(fields, "player_id", "^plr_[A-Za-z0-9_-]{1,128}$");
                if (type == "join_ack")
                {
                    if (!fields.TryGetValue("ok", out var ok) || !(ok is bool accepted)) throw new ArgumentException();
                    string reason = null;
                    if (!accepted) { reason = Field(fields, "reason", "^[a-z_]+$"); PlayServRoomAdmission.ValidateReason(reason); }
                    Ticket entry;
                    lock (_admissionSync)
                    {
                        if (!_tickets.TryGetValue(token, out entry) || !entry.Pending || entry.Offer.RoomName != room || entry.Offer.PlayerId != player) return;
                        entry.Pending = false;
                    }
                    if (!accepted) RejectAdmission(entry, reason);
                    return;
                }
                if (!fields.TryGetValue("expires_in", out var expires) || !(expires is long || expires is int) ||
                    Convert.ToInt64(expires) < 0 || Convert.ToInt64(expires) > int.MaxValue) throw new ArgumentException();
                fields.TryGetValue("params", out var parameters);
                if (parameters != null && !(parameters is IDictionary<string, object>)) throw new ArgumentException();
                var parametersJson = parameters == null ? null : PlayServGameServerJson.Serialize(Canonical(parameters));
                var ttl = (int)Convert.ToInt64(expires);
                var offer = new PlayServTicketOffer(token, room, player, ttl, parametersJson, Seconds);
                var signature = PlayServGameServerJson.Serialize(new { room, player, parameters = parametersJson });
                var bytes = Encoding.UTF8.GetByteCount(json);
                Ticket ticket = null; PlayServTicketDecision immediate = null; bool duplicate = false;
                lock (_admissionSync)
                {
                    if (_tickets.TryGetValue(token, out ticket))
                    {
                        duplicate = true;
                        if (ticket.Signature != signature) immediate = PlayServTicketDecision.Refuse("reservation_invalid");
                        else if (ticket.Spent) immediate = PlayServTicketDecision.Refuse("reservation_consumed");
                        else
                        {
                            ticket.Expires = Math.Min(ticket.Expires, offer.Expires);
                            if (ticket.Expires <= Seconds()) immediate = PlayServTicketDecision.Refuse("reservation_expired");
                        }
                    }
                    else if (!_admissionRooms.ContainsKey(room)) immediate = PlayServTicketDecision.Refuse("room_closed");
                    else if (ttl == 0) immediate = PlayServTicketDecision.Refuse("reservation_expired");
                    else if (_tickets.Count >= _context.AdmissionEntryLimit || bytes > _context.AdmissionByteLimit - _ticketBytes)
                        immediate = PlayServTicketDecision.Refuse("room_refused");
                    else
                    {
                        ticket = new Ticket { Offer = offer, Signature = signature, Bytes = bytes, Expires = offer.Expires, Context = _context };
                        _tickets.Add(token, ticket); _ticketBytes += bytes;
                    }
                }
                // Dispatch asynchronously even when a game callback completes synchronously.
                var received = Seconds();
                _ = Task.Run(() => AnswerOfferAsync(ticket, token, immediate, duplicate, received, ct));
            }
            catch { Report(UplinkErrors.Exception("admission_invalid_frame").UnifiedError); }
        }

        private async Task AnswerOfferAsync(Ticket ticket, string token, PlayServTicketDecision immediate, bool duplicate, double received, CancellationToken connection)
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(connection);
            var remaining = OfferTimeout.TotalSeconds - (Seconds() - received);
            if (remaining <= 0) return;
            budget.CancelAfter(TimeSpan.FromSeconds(remaining));
            var ct = budget.Token;
            try
            {
                var decision = immediate;
                if (decision == null)
                {
                    if (duplicate) { await WaitAsync(ticket.Completion.Task, ct).ConfigureAwait(false); decision = await ticket.Completion.Task.ConfigureAwait(false); }
                    else
                    {
                        var policy = ticket.Context.TicketOfferHandler;
                        var task = policy == null ? Task.FromResult(PlayServTicketDecision.Accept()) :
                            ticket.Context.InvokeAsync(() => { ct.ThrowIfCancellationRequested(); return policy(ticket.Offer, ct); });
                        _ = task.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
                        await WaitAsync(task, ct).ConfigureAwait(false);
                        decision = await task.ConfigureAwait(false);
                    }
                    if (decision == null) return;
                    lock (_admissionSync)
                    {
                        if (!_tickets.TryGetValue(token, out var current) || !ReferenceEquals(current, ticket)) return;
                        if (ticket.Expires <= Seconds()) decision = PlayServTicketDecision.Refuse("reservation_expired");
                        if (ticket.Spent) decision = PlayServTicketDecision.Refuse("reservation_consumed");
                        if (!duplicate) { ticket.Decision = decision; ticket.Completion.TrySetResult(decision); }
                    }
                }
                ct.ThrowIfCancellationRequested();
                if (Seconds() - received >= OfferTimeout.TotalSeconds) return;
                var detail = SafeAdmissionDetail(decision.Detail, token);
                var frame = decision.Ok ? (object)new { type = "ticket_result", reservation_token = token, ok = true } :
                    new { type = "ticket_result", reservation_token = token, ok = false, reason = decision.Reason, detail };
                if (ticket != null && immediate == null)
                    lock (_admissionSync) ticket.Available = decision.Ok && !ticket.Spent;
                await SendAsync(frame, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            { if (ticket != null && immediate == null) lock (_admissionSync) ticket.Available = false; Report(UplinkErrors.Exception("ticket_offer_canceled").UnifiedError); }
            catch
            { if (ticket != null && immediate == null) lock (_admissionSync) ticket.Available = false; Report(UplinkErrors.Exception("ticket_offer_failed").UnifiedError); }
            finally
            {
                if (ticket != null && !duplicate && immediate == null)
                    lock (_admissionSync)
                    {
                        // A failed/late answer must not be replayed later as a successful offer.
                        if (!ticket.Available && ticket.Decision?.Ok != false) { ticket.Spent = true; ticket.Completion.TrySetResult(null); }
                    }
            }
        }

        internal PlayServRoomAdmissionResult AdmitTicket(PlayServGameRoomHandle room, string token, Func<string, string> replacingAdmission = null)
        {
            ValidateTicketToken(token);
            lock (room.AdmissionLock)
            lock (_admissionSync)
            {
                if (_admissionMode != PlayServAdmissionMode.Push) return AdmissionFailure("admission_mode_mismatch");
                if (_admissionSocket == null || _admissionCancellation.IsCancellationRequested) return AdmissionFailure("uplink_not_connected");
                if (!_admissionRooms.TryGetValue(room.RoomName, out var registered) || !ReferenceEquals(room, registered) || !room.CanAdmit || _context.ShuttingDown)
                    return AdmissionFailure("room_closed");
                if (!_tickets.TryGetValue(token, out var ticket)) return AdmissionFailure("reservation_invalid");
                if (ticket.Offer.RoomName != room.RoomName) return AdmissionFailure("room_mismatch");
                if (ticket.Expires <= Seconds()) return AdmissionFailure("reservation_expired");
                if (ticket.Spent) return AdmissionFailure("reservation_consumed");
                if (!ticket.Available) return AdmissionFailure("room_refused");
                var id = Guid.NewGuid().ToString("N");
                if (!room.AddAdmittedPlayer(ticket.Offer.PlayerId, id, replacingAdmission?.Invoke(ticket.Offer.PlayerId))) return AdmissionFailure("room_refused");
                ticket.Spent = true; ticket.Available = false; ticket.Pending = true;
                ticket.AdmissionId = id; ticket.Room = room; ticket.Deadline = Seconds() + ticket.Context.HttpTimeout.TotalSeconds;
                var socket = _admissionSocket; var connection = _admissionCancellation;
                var frame = PlayServGameServerJson.Serialize(new { type = "room_presence", room_name = room.RoomName, @event = "join",
                    seq = ticket.Context.NextPresenceSequence(room.RoomName), player_id = ticket.Offer.PlayerId, reservation_token = token });
                room.QueueAdmissionJoin(() => SendAdmissionJoinAsync(ticket, socket, Encoding.UTF8.GetBytes(frame), connection));
                return new PlayServRoomAdmissionResult(true, ticket.Offer.PlayerId, id, PlayServError.None);
            }
        }
        private async Task SendAdmissionJoinAsync(Ticket ticket, IPlayServUplinkSocket socket, byte[] bytes, CancellationToken connection)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(connection);
            deadline.CancelAfter(ticket.Context.HttpTimeout);
            try
            {
                lock (_admissionSync) { if (!ticket.Pending) return; }
                await socket.SendAsync(bytes, deadline.Token).ConfigureAwait(false);
            }
            catch
            {
                bool reject;
                lock (_admissionSync) { reject = ticket.Pending; ticket.Pending = false; }
                if (reject) RejectAdmission(ticket, "admission_send_outcome_unknown");
                socket.Abort();
            }
        }
        internal async Task ReleaseTicketAsync(PlayServGameRoomHandle room, string token, string reason, string detail, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); ValidateTicketToken(token);
            if (detail != null && detail.Length > 128) throw new ArgumentOutOfRangeException(nameof(detail));
            IPlayServUplinkSocket socket; CancellationToken connection; TimeSpan timeout;
            lock (room.AdmissionLock)
            lock (_admissionSync)
            {
                if (_admissionMode != PlayServAdmissionMode.Push) throw UplinkErrors.Exception("admission_mode_mismatch");
                if (_admissionSocket == null) throw UplinkErrors.Exception("uplink_not_connected");
                if (!_admissionRooms.TryGetValue(room.RoomName, out var registered) || !ReferenceEquals(room, registered) || !room.CanAdmit)
                    throw UplinkErrors.Exception("room_closed");
                if (!_tickets.TryGetValue(token, out var ticket)) throw UplinkErrors.Exception("reservation_invalid");
                if (ticket.Offer.RoomName != room.RoomName) throw UplinkErrors.Exception("room_mismatch");
                if (ticket.Spent) throw UplinkErrors.Exception("reservation_consumed");
                ticket.Spent = true; ticket.Available = false;
                socket = _admissionSocket; connection = _admissionCancellation; timeout = ticket.Context.HttpTimeout;
            }
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct, connection);
            budget.CancelAfter(timeout);
            var bytes = Encoding.UTF8.GetBytes(PlayServGameServerJson.Serialize(new { type = "ticket_release", reservation_token = token, reason, detail = SafeAdmissionDetail(detail, token) }));
            try { await socket.SendAsync(bytes, budget.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { socket.Abort(); throw UplinkErrors.Exception("ticket_release_outcome_unknown"); }
        }
        internal async Task SweepAdmissionsAsync()
        {
            var expired = new List<Ticket>(); var rejected = new List<Ticket>();
            lock (_admissionSync)
            {
                var now = Seconds();
                foreach (var entry in _tickets.Values.ToArray())
                {
                    if (entry.Pending && now >= entry.Deadline) { entry.Pending = false; rejected.Add(entry); }
                    if (now < entry.Expires || entry.Pending) continue;
                    if (entry.Available && !entry.Spent) expired.Add(entry);
                    RemoveTicket(entry);
                }
            }
            foreach (var entry in rejected) RejectAdmission(entry, "admission_ack_timeout");
            // Expiry release is best effort; a slow socket must not stall the liveness monitor.
            CancellationToken connection;
            lock (_admissionSync) connection = _admissionCancellation;
            using var releaseBudget = CancellationTokenSource.CreateLinkedTokenSource(connection);
            releaseBudget.CancelAfter(TimeSpan.FromSeconds(1));
            foreach (var entry in expired)
            {
                if (State != PlayServUplinkState.Connected || releaseBudget.IsCancellationRequested) break;
                try { await SendAsync(new { type = "ticket_release", reservation_token = entry.Offer.ReservationToken, reason = "reservation_expired" }, releaseBudget.Token).ConfigureAwait(false); }
                catch { Report(UplinkErrors.Exception("ticket_release_failed").UnifiedError); break; }
            }
        }
        private void RejectAdmission(Ticket ticket, string reason)
        {
            if (ticket.Room == null) return;
            if (!ticket.Room.RemoveAdmittedPlayer(ticket.Offer.PlayerId, ticket.AdmissionId)) return;
            var result = new PlayServRoomAdmissionResult(false, ticket.Offer.PlayerId, ticket.AdmissionId, UplinkErrors.Exception(reason).UnifiedError);
            ticket.Context.Post(() => ticket.Room.Admission.RaiseRejected(result));
        }
        private static PlayServRoomAdmissionResult AdmissionFailure(string code) => new PlayServRoomAdmissionResult(false, null, null, UplinkErrors.Exception(code).UnifiedError);
        private static void ValidateTicketToken(string token)
        { if (token == null || !Regex.IsMatch(token, "^rsv_[A-Za-z0-9_-]{1,1024}$")) throw new ArgumentException("A valid reservation token is required.", nameof(token)); }
        private static string Field(IDictionary<string, object> fields, string name, string pattern)
        { if (!fields.TryGetValue(name, out var value) || !(value is string text) || !Regex.IsMatch(text, pattern)) throw new ArgumentException(); return text; }
        private static object Canonical(object value)
        {
            if (value is IDictionary<string, object> map) return map.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => Canonical(p.Value));
            if (value is System.Collections.IList list) return list.Cast<object>().Select(Canonical).ToArray();
            return value;
        }
        private static string SafeAdmissionDetail(string detail, string token)
        {
            if (detail == null) return null;
            var safe = PlayServGameServerLogs.Text(detail.Replace(token, "[REDACTED]"));
            return safe.Length > 128 ? safe.Substring(0, 128) : safe;
        }
    }
}
