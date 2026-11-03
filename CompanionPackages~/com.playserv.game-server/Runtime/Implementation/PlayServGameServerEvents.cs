using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Wrapper;

namespace Playserv.GameServer
{
    /// <summary>Publish game-owned events; does not enable client-side event publishing.</summary>
    public sealed class PlayServGameServerEvents
    {
        internal PlayServGameServerEvents() { }

        /// <summary>
        /// Sends a publish frame to a canonical prj_ID:group without adding or changing scope.
        /// The backend enforces project ownership. Completion means socket send only, not receipt by clients.
        /// Reliable selects the platform delivery lane, not an application acknowledgement. No offline queue or retry.
        /// </summary>
        public Task PublishAsync<T>(string group, string eventType, T payload, bool reliable = false, CancellationToken ct = default)
        {
            GameServerUplinkServices.Context(ct);
            GameServerServiceValues.RequireText(group, nameof(group));
            GameServerServiceValues.RequireText(eventType, nameof(eventType));
            var colon = group.IndexOf(':');
            if (!group.StartsWith("prj_", StringComparison.Ordinal) || colon <= 4 || colon == group.Length - 1)
                throw new ArgumentException("Use a canonical prj_ID:group name.", nameof(group));
            var bytes = GameServerUplinkServices.Frame(new { type = "publish", group, event_type = eventType, data = payload, reliable });
            return PlayServGameServer.Uplink.SendPreparedAsync(bytes, ct);
        }
    }

    internal static class GameServerUplinkServices
    {
        internal static PlayServGameServerContext Context(CancellationToken ct)
        {
            PlayServGameServer.EnsureSupportedBuildForServices();
            ct.ThrowIfCancellationRequested();
            var context = PlayServGameServer.GetContextForServices();
            if (context.ShuttingDown) throw UplinkErrors.Exception("server_shutting_down");
            return context;
        }

        internal static byte[] Frame(object frame)
        {
            byte[] bytes;
            try { bytes = Encoding.UTF8.GetBytes(PlayServGameServerJson.Serialize(frame)); }
            catch { throw new PlayServGameServerException(new PlayServError(PlayServErrorCode.Serialization,
                "uplink_serialization_failed", "The uplink frame could not be serialized.")); }
            if (bytes.Length > PlayServUplinkSocket.MaxFrameBytes) throw UplinkErrors.Exception("frame_too_large");
            return bytes;
        }
    }
}
