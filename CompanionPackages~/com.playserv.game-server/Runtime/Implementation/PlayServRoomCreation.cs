using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Serialization;
using Playserv.Wrapper;

namespace Playserv.GameServer
{
    /// <summary>Game-owned local preparation. Do not register the room from this callback; honor cancellation and clean up locally.</summary>
    public delegate Task<PlayServRoomCreateDecision> PlayServRoomFactory(PlayServRoomCreateContext request, CancellationToken ct);

    /// <summary>Only the facts required to prepare a named room.</summary>
    public sealed class PlayServRoomCreateContext
    {
        private readonly string _attributesJson;
        internal PlayServRoomCreateContext(string name, PlayServRoomConfiguration configuration)
            : this(name, configuration, null) { }
        internal PlayServRoomCreateContext(string name, PlayServRoomConfiguration configuration, string attributesJson)
        { RoomName = name; Configuration = configuration; _attributesJson = attributesJson; }
        public string RoomName { get; }
        public PlayServRoomConfiguration Configuration { get; }
        /// <summary>
        /// Requested content, not authoritative configuration. Returns an independent JSON-object copy,
        /// or null when omitted. Apply, alter or ignore it in the returned room snapshot, or refuse the
        /// request with ContentRefused. No attributes are automatically merged into registration.
        /// </summary>
        public object Attributes => _attributesJson == null ? null : RoomCreateAttributes.Read<Dictionary<string, object>>(_attributesJson);
    }

    /// <summary>Contract-defined refusals; there are no custom wire reason codes.</summary>
    public enum PlayServRoomCreateRefusal
    {
        RoomNameConflict = 0,
        InstanceDraining = 1,
        /// <summary>Temporary capacity refusal: the instance remains a candidate for later requests.</summary>
        RoomQuotaExceeded = 2,
        /// <summary>Room preparation failed: the instance remains a candidate for later requests.</summary>
        RoomCreateFailed = 3,
        /// <summary>The game declined the requested content; the instance remains a candidate.</summary>
        ContentRefused = 4
    }

    /// <summary>Prepared room snapshot or a bounded contract refusal, returned by the game factory.</summary>
    public sealed class PlayServRoomCreateDecision
    {
        private PlayServRoomCreateDecision(PlayServGameRoomSnapshot snapshot, string reason, string detail)
        { Snapshot = snapshot; Reason = reason; Detail = detail; }
        public PlayServGameRoomSnapshot Snapshot { get; }
        public string Reason { get; }
        public string Detail { get; }
        public bool IsAccepted => Snapshot != null;
        public static PlayServRoomCreateDecision Accept(PlayServGameRoomSnapshot snapshot) =>
            new PlayServRoomCreateDecision(snapshot ?? throw new ArgumentNullException(nameof(snapshot)), null, null);
        public static PlayServRoomCreateDecision Refuse(PlayServRoomCreateRefusal reason, string detail = null)
        {
            string wireReason;
            switch (reason)
            {
                case PlayServRoomCreateRefusal.RoomNameConflict: wireReason = "room_name_conflict"; break;
                case PlayServRoomCreateRefusal.InstanceDraining: wireReason = "instance_draining"; break;
                case PlayServRoomCreateRefusal.RoomQuotaExceeded: wireReason = "room_quota_exceeded"; break;
                case PlayServRoomCreateRefusal.RoomCreateFailed: wireReason = "room_create_failed"; break;
                case PlayServRoomCreateRefusal.ContentRefused: wireReason = "content_refused"; break;
                default: throw new ArgumentOutOfRangeException(nameof(reason));
            }
            if (detail != null && (detail.Length > 128 || Array.Exists(detail.ToCharArray(), char.IsControl)))
                throw new ArgumentException("Refusal detail must be at most 128 characters without control characters.", nameof(detail));
            return new PlayServRoomCreateDecision(null, wireReason, detail);
        }
    }

    /// <summary>Local completion notification, including failures with an unknown remote registration outcome.</summary>
    public sealed class PlayServRoomCreationOutcome
    {
        internal PlayServRoomCreationOutcome(string name, PlayServGameRoomHandle room, PlayServError error, bool factoryInvoked)
        { RoomName = name; Room = room; Error = error ?? PlayServError.None; FactoryInvoked = factoryInvoked; }
        public string RoomName { get; }
        public PlayServGameRoomHandle Room { get; }
        public PlayServError Error { get; }
        /// <summary>False for early refusals, including duplicates: do not tear down another request's preparation.</summary>
        public bool FactoryInvoked { get; }
        public bool IsSuccess => Room != null && !Error.IsError;
    }

    [Serializable] internal sealed class RoomCreateWire { public string room_name; public object attributes; }

    // Scoped to room_create: changing general companion JSON parsing would affect existing game paths.
    internal static class RoomCreateAttributes
    {
        private static readonly IJsonCodec Codec = new NewtonsoftJsonCodec();
        private static readonly JsonCodecOptions Options = new JsonCodecOptions { ParseDates = false, IgnoreMetadataProperties = true };
        internal static T Read<T>(string json) => Codec.Deserialize<T>(json, Options);

        internal static string Capture(object value)
        {
            var plain = Codec.ToPlainValue(value);
            if (plain == null) return null;
            if (!(plain is IDictionary<string, object> attributes)) throw InvalidAttributes();
            foreach (var item in attributes.Values)
            {
                if (item == null || item is string || item is bool || item is long || item is BigInteger) continue;
                if (item is double number && !double.IsNaN(number) && !double.IsInfinity(number)) continue;
                throw InvalidAttributes(); // No nested objects/arrays or non-JSON scalar types.
            }
            var json = Codec.Serialize(attributes, Options);
            if (Encoding.UTF8.GetByteCount(json) > 2048) throw InvalidAttributes();
            return json;
        }

        private static ArgumentException InvalidAttributes() => new ArgumentException(
            "Requested room attributes must be a single-level JSON object of at most 2048 UTF-8 bytes.");
    }
}
