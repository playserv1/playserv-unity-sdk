using System;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Wrapper;

namespace Playserv.GameServer
{
    /// <summary>The path confirmed by the platform, not merely the requested capability.</summary>
    public enum PlayServAdmissionMode { Unknown, Consume, Push }
    public delegate Task<PlayServTicketDecision> PlayServTicketOfferHandler(PlayServTicketOffer offer, CancellationToken ct);

    /// <summary>In-memory offer. Never log ReservationToken or Params; each Params read is a defensive copy.</summary>
    public sealed class PlayServTicketOffer
    {
        private readonly string _params;
        private readonly Func<double> _clock;
        private readonly double _expires;
        internal PlayServTicketOffer(string token, string room, string player, int ttl, string parameters, Func<double> clock)
        { ReservationToken = token; RoomName = room; PlayerId = player; ExpiresIn = ttl; _params = parameters; _clock = clock; _expires = clock() + ttl; }
        public string ReservationToken { get; }
        public string RoomName { get; }
        public string PlayerId { get; }
        public int ExpiresIn { get; }
        public TimeSpan RemainingLifetime => TimeSpan.FromSeconds(Math.Max(0, _expires - _clock()));
        public object Params => PlayServGameServerJson.ParseOptionalObject(_params);
        internal double Expires => _expires;
    }

    public sealed class PlayServTicketDecision
    {
        private PlayServTicketDecision(bool ok, string reason, string detail) { Ok = ok; Reason = reason; Detail = detail; }
        public bool Ok { get; }
        public string Reason { get; }
        public string Detail { get; }
        public static PlayServTicketDecision Accept() => new PlayServTicketDecision(true, null, null);
        /// <summary>Only contract reasons are permitted. Detail is credential-redacted before transmission.</summary>
        public static PlayServTicketDecision Refuse(string reason = "room_refused", string detail = null)
        {
            PlayServRoomAdmission.ValidateReason(reason);
            if (detail != null && detail.Length > 128) throw new ArgumentOutOfRangeException(nameof(detail));
            return new PlayServTicketDecision(false, reason, detail);
        }
    }

    /// <summary>Local outcome, not a platform acknowledgement. AdmissionId identifies this exact game connection.</summary>
    public sealed class PlayServRoomAdmissionResult
    {
        internal PlayServRoomAdmissionResult(bool ok, string player, string id, PlayServError error)
        { Ok = ok; PlayerId = player; AdmissionId = id; Error = error; }
        public bool Ok { get; }
        public string PlayerId { get; }
        public string AdmissionId { get; }
        public PlayServError Error { get; }
    }

    /// <summary>Local ticket admission for one room. The game owns actual network acceptance and disconnection.</summary>
    public sealed class PlayServRoomAdmission
    {
        private readonly PlayServGameRoomHandle _room;
        internal PlayServRoomAdmission(PlayServGameRoomHandle room) { _room = room; }
        /// <summary>Disconnect the connection identified by AdmissionId. Delivered on Unity's context, outside mutation locks.</summary>
        public event Action<PlayServRoomAdmissionResult> AdmissionRejected;
        /// <summary>Atomically consumes a local ticket and queues one join without waiting for join_ack.</summary>
        public PlayServRoomAdmissionResult TryAdmit(string reservationToken) => PlayServGameServer.Uplink.AdmitTicket(_room, reservationToken);
        /// <summary>Releases an unused ticket. No offline queue/replay; completion confirms send only.</summary>
        public Task ReleaseAsync(string reservationToken, string reason = "room_refused", string detail = null, CancellationToken ct = default)
        { ValidateReason(reason); return PlayServGameServer.Uplink.ReleaseTicketAsync(_room, reservationToken, reason, detail, ct); }
        internal void RaiseRejected(PlayServRoomAdmissionResult result) => AdmissionRejected?.Invoke(result);
        internal static void ValidateReason(string reason)
        {
            switch (reason)
            {
                case "reservation_expired": case "reservation_consumed": case "reservation_invalid":
                case "room_mismatch": case "room_closed": case "room_refused": return;
                default: throw new ArgumentException("Unsupported reservation reason.", nameof(reason));
            }
        }
    }
}
