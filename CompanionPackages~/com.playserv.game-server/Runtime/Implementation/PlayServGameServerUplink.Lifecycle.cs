using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Playserv.GameServer
{
    public sealed partial class PlayServGameServerUplink
    {
        /// <summary>Ordered transition notifications on the configured Unity context. The property may already reflect a later transition.</summary>
        public event Action<PlayServUplinkState> StateChanged;
        /// <summary>A newer authoritative configuration, or null when configuration becomes unavailable. REST reads do not raise this event.</summary>
        public event Action<PlayServRoomConfiguration> ConfigurationChanged;
        private readonly Queue<Action> _lifecycle = new Queue<Action>();
        private bool _lifecycleScheduled;

        private void SetStateLocked(PlayServUplinkState state)
        {
            if (_state == state) return;
            _state = state;
            QueueLifecycle(() => StateChanged?.Invoke(state));
        }
        private void ClearConfigurationLocked()
        {
            if (_configuration == null) return;
            _configuration = null;
            QueueLifecycle(() => ConfigurationChanged?.Invoke(null));
        }
        private void QueueLifecycle(Action notification)
        {
            lock (_sync)
            {
                var context = _context;
                if (context == null) return;
                _lifecycle.Enqueue(() => context.Post(() =>
                {
                    if (ReferenceEquals(PlayServGameServer.Uplink, this)) notification();
                }));
                if (_lifecycleScheduled) return;
                _lifecycleScheduled = true;
                ThreadPool.QueueUserWorkItem(_ => DrainLifecycle());
            }
        }
        private void DrainLifecycle()
        {
            while (true)
            {
                Action next;
                lock (_sync)
                {
                    if (_lifecycle.Count == 0) { _lifecycleScheduled = false; return; }
                    next = _lifecycle.Dequeue();
                }
                try { next(); } catch { /* A dispatcher failure cannot break the receive loop. */ }
            }
        }
    }

    public static partial class PlayServGameServer
    {
        /// <summary>Reads platform-owned configuration with server authorization. Does not modify an active uplink or room.</summary>
        public static async Task<PlayServRoomConfiguration> GetRoomConfigurationAsync(string functionSlug, CancellationToken ct = default)
        {
            EnsureSupportedBuild(); ct.ThrowIfCancellationRequested();
            var context = GetContext(); var slug = ValidateFunctionSlug(functionSlug);
            var response = await SendAsync(context, "GET", "rooms/" + Escape(slug) + "/config", null,
                context.HttpTimeout, "room configuration", false, ct);
            try
            {
                var fields = GameServerServiceValues.Object(response.Body);
                int Read(string key, int minimum, bool nullable = false)
                {
                    if (fields == null) throw new FormatException();
                    if (!fields.TryGetValue(key, out var value)) { if (nullable) return -1; throw new FormatException(); }
                    if (nullable && value == null) return -1;
                    var integer = value is long number ? number : value is int small ? small : -1;
                    if (integer < minimum || integer > int.MaxValue) throw new FormatException();
                    return (int)integer;
                }
                var wire = new RoomConfigWire
                {
                    capacity = Read("capacity", 1), reservation_ttl_seconds = Read("reservation_ttl_seconds", 1),
                    room_lifetime_seconds = Read("room_lifetime_seconds", 0), max_rooms = Read("max_rooms", 1),
                    version = Read("version", 0)
                };
                var idle = Read("room_idle_timeout_seconds", 0, true);
                wire.room_idle_timeout_seconds = idle < 0 ? (int?)null : idle;
                return new PlayServRoomConfiguration(wire);
            }
            catch { throw InvalidResponse("room configuration"); }
        }
    }
}
