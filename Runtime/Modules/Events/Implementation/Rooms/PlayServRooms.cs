using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Events;

#nullable enable

namespace Playserv.Wrapper
{
    /// <summary>
    /// Generic room runtime for server-side RPC code.
    /// Provides per-room state storage, serialized command execution, and stable tick loops.
    /// </summary>
    public static class PlayServRooms
    {
        public const int MaxTickRate = 60;
        public const int MaxRooms = 4096;

        private const double MaxDeltaSeconds = 0.25;

        private static readonly object RoomsGate = new object();
        private static readonly Dictionary<string, RoomRuntime> Rooms =
            new Dictionary<string, RoomRuntime>(StringComparer.Ordinal);

        public static void StartLoop(string roomId, int tickRate, Action<RoomTickContext> onTick)
        {
            StartLoop(roomId, tickRate, onTick, null);
        }

        public static void StartLoop(
            string roomId,
            int tickRate,
            Action<RoomTickContext> onTick,
            RoomLoopOptions? options)
        {
            if (onTick == null)
                throw new ArgumentNullException(nameof(onTick));

            StartLoop(roomId, tickRate, context =>
            {
                onTick(context);
                return Task.CompletedTask;
            }, options);
        }

        public static void StartLoop(string roomId, int tickRate, Func<RoomTickContext, Task> onTick)
        {
            StartLoop(roomId, tickRate, onTick, null);
        }

        public static void StartLoop(
            string roomId,
            int tickRate,
            Func<RoomTickContext, Task> onTick,
            RoomLoopOptions? options)
        {
            if (onTick == null)
                throw new ArgumentNullException(nameof(onTick));

            ValidateTickRate(tickRate);
            var room = GetOrCreateRuntime(roomId);
            var periodMs = TickRateToPeriodMs(tickRate);

            lock (room.Gate)
            {
                room.TickRate = tickRate;
                room.Options = RoomLoopOptions.Normalize(options);
                room.OnTick = onTick;
                room.IsStopped = false;
                room.StoppedReason = null;
                room.LastTickUtc = DateTime.UtcNow;
                room.LastCommandUtc = room.LastCommandUtc == default ? room.LastTickUtc : room.LastCommandUtc;
                room.LastActivityUtc = room.LastTickUtc;
                room.ConsecutiveErrorCount = 0;

                if (room.Timer == null)
                    room.Timer = new Timer(_ => RunRoomTick(room), null, periodMs, periodMs);
                else
                    room.Timer.Change(periodMs, periodMs);
            }
        }

        public static void StopLoop(string roomId)
        {
            if (!TryGetRuntime(roomId, out var room))
                return;

            lock (room.Gate)
            {
                room.IsStopped = true;
                room.StoppedReason = "Stopped";
                room.Timer?.Dispose();
                room.Timer = null;
                room.OnTick = null;
            }
        }

        public static void DestroyRoom(string roomId)
        {
            RoomRuntime? room;
            lock (RoomsGate)
            {
                if (!Rooms.TryGetValue(NormalizeRoomId(roomId), out room))
                    return;

                Rooms.Remove(room.RoomId);
            }

            lock (room.Gate)
            {
                room.IsStopped = true;
                room.StoppedReason = "Destroyed";
                room.Timer?.Dispose();
                room.Timer = null;
                room.OnTick = null;
                room.Items.Clear();
            }
        }

        public static TResult Enqueue<TResult>(string roomId, Func<RoomContext, TResult> command)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            var room = GetOrCreateRuntime(roomId);
            lock (room.Gate)
            {
                room.LastActivityUtc = DateTime.UtcNow;
                room.LastCommandUtc = room.LastActivityUtc;
                return command(new RoomContext(room));
            }
        }

        public static void Enqueue(string roomId, Action<RoomContext> command)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            Enqueue<object>(roomId, context =>
            {
                command(context);
                return NullCommandResult.Instance;
            });
        }

        public static void PublishToRoom<TEvent>(string roomId, TEvent @event)
            where TEvent : Event
        {
            if (string.IsNullOrWhiteSpace(roomId))
                throw new ArgumentException("Room id is required.", nameof(roomId));

            if (@event == null)
                throw new ArgumentNullException(nameof(@event));

            PlayServEvents.PublishForGroup(roomId, @event);
        }

        public static bool Exists(string roomId)
        {
            lock (RoomsGate)
            {
                return Rooms.ContainsKey(NormalizeRoomId(roomId));
            }
        }

        public static bool TryGetDiagnostics(string roomId, out RoomDiagnostics diagnostics)
        {
            if (!TryGetRuntime(roomId, out var room))
            {
                diagnostics = RoomDiagnostics.Empty;
                return false;
            }

            lock (room.Gate)
            {
                diagnostics = RoomDiagnostics.From(room);
                return true;
            }
        }

        public static RoomDiagnostics GetDiagnostics(string roomId)
        {
            if (TryGetDiagnostics(roomId, out var diagnostics))
                return diagnostics;

            throw new KeyNotFoundException($"Room '{NormalizeRoomId(roomId)}' was not found.");
        }

        public static int RoomCount
        {
            get
            {
                lock (RoomsGate)
                {
                    return Rooms.Count;
                }
            }
        }

        private static void RunRoomTick(RoomRuntime room)
        {
            if (Interlocked.Exchange(ref room.TickRunning, 1) == 1)
            {
                Interlocked.Increment(ref room.SkippedTickCount);
                return;
            }

            var lockTaken = false;
            try
            {
                if (!Monitor.TryEnter(room.Gate))
                {
                    Interlocked.Increment(ref room.SkippedTickCount);
                    return;
                }

                lockTaken = true;
                if (room.IsStopped || room.OnTick == null)
                    return;

                var now = DateTime.UtcNow;
                if (ShouldStopIdleRoom(room, now))
                    return;

                var delta = now - room.LastTickUtc;
                room.LastTickUtc = now;
                room.LastActivityUtc = now;
                room.Tick++;

                var deltaSeconds = (float)Math.Max(0.0, Math.Min(delta.TotalSeconds, MaxDeltaSeconds));
                var context = new RoomTickContext(room, room.Tick, deltaSeconds, now);
                var started = DateTime.UtcNow;
                var task = room.OnTick(context);
                if (task != null && !task.IsCompleted)
                    task.GetAwaiter().GetResult();

                room.ConsecutiveErrorCount = 0;
                var elapsed = DateTime.UtcNow - started;
                if (elapsed > room.Options.SlowTickThreshold)
                {
                    room.SlowTickCount++;
                    room.LastSlowTickDuration = elapsed;
                    room.LastSlowTickAt = DateTime.UtcNow;
                }
            }
            catch (Exception ex)
            {
                room.LastError = ex;
                room.LastErrorAt = DateTime.UtcNow;
                room.ConsecutiveErrorCount++;
                if (room.Options.MaxConsecutiveErrors > 0 &&
                    room.ConsecutiveErrorCount >= room.Options.MaxConsecutiveErrors)
                {
                    StopRoomLocked(room, "Stopped after repeated tick errors.");
                }
            }
            finally
            {
                if (lockTaken)
                    Monitor.Exit(room.Gate);

                Interlocked.Exchange(ref room.TickRunning, 0);
            }
        }

        private static RoomRuntime GetOrCreateRuntime(string roomId)
        {
            var normalized = NormalizeRoomId(roomId);
            lock (RoomsGate)
            {
                if (Rooms.TryGetValue(normalized, out var existing))
                    return existing;

                if (Rooms.Count >= MaxRooms)
                    throw new InvalidOperationException($"Room limit reached. Max rooms per process: {MaxRooms}.");

                var room = new RoomRuntime(normalized);
                Rooms.Add(normalized, room);
                return room;
            }
        }

        private static bool TryGetRuntime(string roomId, out RoomRuntime room)
        {
            lock (RoomsGate)
            {
                return Rooms.TryGetValue(NormalizeRoomId(roomId), out room!);
            }
        }

        private static string NormalizeRoomId(string roomId)
        {
            if (string.IsNullOrWhiteSpace(roomId))
                throw new ArgumentException("Room id is required.", nameof(roomId));

            return roomId.Trim();
        }

        private static void ValidateTickRate(int tickRate)
        {
            if (tickRate <= 0)
                throw new ArgumentOutOfRangeException(nameof(tickRate), "Tick rate must be greater than zero.");

            if (tickRate > MaxTickRate)
                throw new ArgumentOutOfRangeException(nameof(tickRate), $"Tick rate must be less than or equal to {MaxTickRate}.");
        }

        private static int TickRateToPeriodMs(int tickRate)
        {
            return Math.Max(1, (int)Math.Round(1000.0 / tickRate));
        }

        private static bool ShouldStopIdleRoom(RoomRuntime room, DateTime now)
        {
            var idleTimeout = room.Options.IdleTimeout;
            if (!idleTimeout.HasValue || idleTimeout.Value <= TimeSpan.Zero)
                return false;

            if (now - room.LastCommandUtc < idleTimeout.Value)
                return false;

            if (room.Options.DestroyOnIdle)
            {
                DestroyRoom(room.RoomId);
            }
            else
            {
                StopRoomLocked(room, "Idle timeout.");
            }

            return true;
        }

        private static void StopRoomLocked(RoomRuntime room, string reason)
        {
            room.IsStopped = true;
            room.StoppedReason = reason;
            room.Timer?.Dispose();
            room.Timer = null;
            room.OnTick = null;
        }

        internal sealed class RoomRuntime
        {
            public readonly object Gate = new object();
            public readonly Dictionary<Type, object> Items = new Dictionary<Type, object>();

            public RoomRuntime(string roomId)
            {
                RoomId = roomId;
                LastTickUtc = DateTime.UtcNow;
                LastActivityUtc = LastTickUtc;
            }

            public string RoomId { get; }
            public int TickRate { get; set; }
            public long Tick { get; set; }
            public DateTime LastTickUtc { get; set; }
            public DateTime LastActivityUtc { get; set; }
            public DateTime LastCommandUtc { get; set; }
            public Func<RoomTickContext, Task>? OnTick { get; set; }
            public Timer? Timer { get; set; }
            public RoomLoopOptions Options { get; set; } = RoomLoopOptions.Default;
            public Exception? LastError { get; set; }
            public DateTime? LastErrorAt { get; set; }
            public int ConsecutiveErrorCount { get; set; }
            public long SkippedTickCount;
            public long SlowTickCount;
            public TimeSpan LastSlowTickDuration { get; set; }
            public DateTime? LastSlowTickAt { get; set; }
            public bool IsStopped { get; set; }
            public string? StoppedReason { get; set; }
            public int TickRunning;
        }

        private sealed class NullCommandResult
        {
            public static readonly NullCommandResult Instance = new NullCommandResult();

            private NullCommandResult()
            {
            }
        }
    }

    public sealed class RoomLoopOptions
    {
        public static RoomLoopOptions Default => new RoomLoopOptions();

        public TimeSpan? IdleTimeout { get; set; }
        public bool DestroyOnIdle { get; set; } = true;
        public int MaxConsecutiveErrors { get; set; } = 5;
        public TimeSpan SlowTickThreshold { get; set; } = TimeSpan.FromMilliseconds(100);

        internal static RoomLoopOptions Normalize(RoomLoopOptions? options)
        {
            if (options == null)
                return Default.Clone();

            if (options.MaxConsecutiveErrors < 0)
                throw new ArgumentOutOfRangeException(nameof(options), "MaxConsecutiveErrors must be greater than or equal to zero.");

            if (options.SlowTickThreshold <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(options), "SlowTickThreshold must be greater than zero.");

            return options.Clone();
        }

        private RoomLoopOptions Clone()
        {
            return new RoomLoopOptions
            {
                IdleTimeout = IdleTimeout,
                DestroyOnIdle = DestroyOnIdle,
                MaxConsecutiveErrors = MaxConsecutiveErrors,
                SlowTickThreshold = SlowTickThreshold
            };
        }
    }

    public sealed class RoomDiagnostics
    {
        public static readonly RoomDiagnostics Empty = new RoomDiagnostics();

        public string? RoomId { get; private set; }
        public int TickRate { get; private set; }
        public long Tick { get; private set; }
        public bool IsLoopRunning { get; private set; }
        public string? StoppedReason { get; private set; }
        public DateTime LastTickUtc { get; private set; }
        public DateTime LastCommandUtc { get; private set; }
        public DateTime LastActivityUtc { get; private set; }
        public Exception? LastError { get; private set; }
        public DateTime? LastErrorAt { get; private set; }
        public int ConsecutiveErrorCount { get; private set; }
        public long SkippedTickCount { get; private set; }
        public long SlowTickCount { get; private set; }
        public TimeSpan LastSlowTickDuration { get; private set; }
        public DateTime? LastSlowTickAt { get; private set; }

        internal static RoomDiagnostics From(PlayServRooms.RoomRuntime room)
        {
            return new RoomDiagnostics
            {
                RoomId = room.RoomId,
                TickRate = room.TickRate,
                Tick = room.Tick,
                IsLoopRunning = !room.IsStopped && room.Timer != null,
                StoppedReason = room.StoppedReason,
                LastTickUtc = room.LastTickUtc,
                LastCommandUtc = room.LastCommandUtc,
                LastActivityUtc = room.LastActivityUtc,
                LastError = room.LastError,
                LastErrorAt = room.LastErrorAt,
                ConsecutiveErrorCount = room.ConsecutiveErrorCount,
                SkippedTickCount = room.SkippedTickCount,
                SlowTickCount = room.SlowTickCount,
                LastSlowTickDuration = room.LastSlowTickDuration,
                LastSlowTickAt = room.LastSlowTickAt
            };
        }
    }

    public class RoomContext
    {
        private readonly PlayServRooms.RoomRuntime _room;

        internal RoomContext(PlayServRooms.RoomRuntime room)
        {
            _room = room;
        }

        public string RoomId => _room.RoomId;
        public long Tick => _room.Tick;
        public DateTime LastActivityUtc => _room.LastActivityUtc;
        public Exception? LastError => _room.LastError;

        public T Get<T>()
            where T : class
        {
            if (!TryGet<T>(out var value))
                throw new KeyNotFoundException($"Room state '{typeof(T).FullName}' was not found.");

            return value;
        }

        public T GetOrCreate<T>()
            where T : class, new()
        {
            return GetOrCreate(() => new T());
        }

        public T GetOrCreate<T>(Func<T> factory)
            where T : class
        {
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));

            var type = typeof(T);
            if (_room.Items.TryGetValue(type, out var existing))
                return (T)existing;

            var value = factory();
            if (value == null)
                throw new InvalidOperationException($"Room state factory returned null for '{type.FullName}'.");

            _room.Items[type] = value;
            return value;
        }

        public void Set<T>(T value)
            where T : class
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));

            _room.Items[typeof(T)] = value;
        }

        public bool TryGet<T>(out T value)
            where T : class
        {
            if (_room.Items.TryGetValue(typeof(T), out var existing) && existing is T typed)
            {
                value = typed;
                return true;
            }

            value = null!;
            return false;
        }

        public bool Remove<T>()
            where T : class
        {
            return _room.Items.Remove(typeof(T));
        }
    }

    public sealed class RoomTickContext : RoomContext
    {
        internal RoomTickContext(PlayServRooms.RoomRuntime room, long tick, float deltaTime, DateTime serverTime)
            : base(room)
        {
            Tick = tick;
            DeltaTime = deltaTime;
            ServerTime = serverTime;
        }

        public new long Tick { get; }
        public float DeltaTime { get; }
        public DateTime ServerTime { get; }
    }
}

#nullable restore
