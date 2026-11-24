using System;
using System.Threading;
using System.Threading.Tasks;

namespace Playserv.GameServer
{
    public static partial class PlayServGameServer
    {
        private static PlayServHostedRoom _hostedRoom;

        /// <summary>Starts the room assigned to this process. Call on Unity's main thread before configuring manual hosting.</summary>
        public static Task<PlayServHostedRoom> StartHostedRoomAsync(IPlayServHostedRoomTransport transport,
            PlayServHostedRoomOptions options = null, CancellationToken cancellationToken = default)
        {
            EnsureSupportedBuild();
            cancellationToken.ThrowIfCancellationRequested();
            options = options ?? new PlayServHostedRoomOptions();
            return StartHostedRoomAsync(transport, options, PlayServServerLaunch.ReadHosted(options.Transport), null, cancellationToken);
        }

        internal static async Task<PlayServHostedRoom> StartHostedRoomAsync(IPlayServHostedRoomTransport transport,
            PlayServHostedRoomOptions options, PlayServHostedLaunch launch, IPlayServGameServerTransport http, CancellationToken ct)
        {
            EnsureSupportedBuild();
            ct.ThrowIfCancellationRequested();
            if (transport == null) throw new ArgumentNullException(nameof(transport));
            if (SynchronizationContext.Current == null)
                throw new InvalidOperationException("Start hosted rooms on Unity's main thread.");
            var server = options.Configure(launch);
            var hosted = new PlayServHostedRoom(transport, options, launch);
            lock (ConfigurationGate)
            lock (Sync)
            {
                if (_hostedRoom != null || _context != null || Rooms.Count != 0 || StartingRooms.Count != 0 ||
                    Uplink.State != PlayServUplinkState.Disconnected)
                    throw new InvalidOperationException("A hosted launch or manual server configuration already owns this process.");
                _hostedRoom = hosted;
                try
                {
                    ConfigureCore(server, http, null, null, true);
                    hosted.Context = _context; hosted.Uplink = Uplink;
                }
                catch { _hostedRoom = null; throw; }
            }
            return await hosted.StartAsync(ct);
        }

        internal static bool OwnsHostedRoom(PlayServHostedRoom hosted)
        { lock (Sync) return ReferenceEquals(_hostedRoom, hosted); }

        internal static void ReleaseHostedRoom(PlayServHostedRoom hosted)
        { lock (Sync) { if (ReferenceEquals(_hostedRoom, hosted)) _hostedRoom = null; } }

        internal static void FinishHostedCleanup(PlayServHostedRoom hosted, bool retain)
        {
            lock (ConfigurationGate)
            {
                lock (Sync)
                {
                    if (!ReferenceEquals(_hostedRoom, hosted)) return;
                    if (hosted.Context != null) hosted.Context.ShuttingDown = true;
                    if (ReferenceEquals(Uplink, hosted.Uplink)) hosted.Uplink.CancelForModuleShutdown();
                    if (ReferenceEquals(_context, hosted.Context)) _context = null;
                    foreach (var room in Rooms.Values) room.CancelLocally();
                    Rooms.Clear(); StartingRooms.Clear(); Generations.Clear();
                    if (!retain) _hostedRoom = null;
                }
                Logs.ClearForContext(hosted.Context);
            }
        }
    }
}
