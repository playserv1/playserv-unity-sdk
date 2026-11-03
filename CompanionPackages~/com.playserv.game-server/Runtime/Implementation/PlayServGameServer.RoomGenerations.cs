using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Playserv.GameServer
{
    internal sealed class RoomGeneration
    {
        internal readonly CancellationTokenSource Stop = new CancellationTokenSource();
        internal readonly HashSet<string> RemovedPlayers = new HashSet<string>(StringComparer.Ordinal);
        internal readonly PlayServGameServerContext Context;
        internal readonly string Key, Name;
        internal readonly PlayServGameServerUplink Uplink;
        internal volatile bool OperatorClosing;
        internal bool OperatorOnly, ReleasePending;
        internal int OperatorActions;
        internal RoomGeneration(PlayServGameServerContext context, string key, string name, PlayServGameServerUplink uplink)
        { Context = context; Key = key; Name = name; Uplink = uplink; }
        internal bool PlayerRemoved(string player) { lock (RemovedPlayers) return RemovedPlayers.Contains(player); }
        internal void RemovePlayer(string player) { lock (RemovedPlayers) RemovedPlayers.Add(player); }
    }

    public static partial class PlayServGameServer
    {
        private static readonly Dictionary<string, RoomGeneration> Generations = new Dictionary<string, RoomGeneration>(StringComparer.Ordinal);

        private static RoomGeneration NewGeneration(PlayServGameServerContext context, string key, string name)
        {
            var generation = new RoomGeneration(context, key, name, Uplink);
            Generations[key] = generation;
            return generation;
        }

        internal static RoomGeneration FindGeneration(PlayServGameServerContext context, string slug, string name)
        {
            lock (Sync) return ReferenceEquals(context, _context) && Generations.TryGetValue(BuildRoomKey(slug, name), out var generation) ? generation : null;
        }

        internal static RoomGeneration BeginOperatorAction(PlayServGameServerContext context, string slug, string name,
            string player, Func<object, Task<bool>> reportLeave, out Task<bool> reporting)
        {
            reporting = Task.FromResult(true);
            RoomGeneration generation;
            PlayServGameRoomHandle room;
            lock (Sync)
            {
                var key = BuildRoomKey(slug, name);
                if (!ReferenceEquals(context, _context)) return null;
                if (!Generations.TryGetValue(key, out generation))
                {
                    generation = NewGeneration(context, key, name);
                    generation.OperatorOnly = true;
                    StartingRooms.Add(key);
                }
                Rooms.TryGetValue(key, out room);
                generation.OperatorActions++;
                if (player == null)
                {
                    generation.OperatorClosing = true;
                    Rooms.Remove(key);
                    StartingRooms.Add(key); // Keep this generation reserved while game cleanup may still touch its name.
                }
                else generation.RemovePlayer(player);
            }
            if (player == null)
            {
                try { generation.Stop.Cancel(); } catch { }
                generation.Uplink.ClearAdmissionRoom(name, generation);
                room?.CloseFromOperator();
            }
            else
            {
                generation.Uplink.RevokePlayerTickets(name, player, generation);
                if (room != null) reporting = room.RemoveFromOperator(player, reportLeave);
            }
            return generation;
        }

        internal static void EndOperatorAction(RoomGeneration generation)
        {
            if (generation == null) return;
            lock (Sync)
            {
                generation.OperatorActions--;
                if ((generation.OperatorClosing || generation.OperatorOnly || generation.ReleasePending) && generation.OperatorActions == 0 &&
                    Generations.TryGetValue(generation.Key, out var current) && ReferenceEquals(current, generation))
                { StartingRooms.Remove(generation.Key); Generations.Remove(generation.Key); }
            }
        }

        private static void ReleaseGeneration(RoomGeneration generation)
        {
            if (generation == null) return;
            lock (Sync)
            {
                if (!Generations.TryGetValue(generation.Key, out var current) || !ReferenceEquals(current, generation)) return;
                if (generation.OperatorClosing && generation.OperatorActions > 0) return;
                if (!Rooms.ContainsKey(generation.Key) && generation.OperatorActions > 0)
                {
                    generation.ReleasePending = true;
                    StartingRooms.Add(generation.Key);
                    return;
                }
                StartingRooms.Remove(generation.Key);
                if (!Rooms.ContainsKey(generation.Key)) Generations.Remove(generation.Key);
            }
        }
    }
}
