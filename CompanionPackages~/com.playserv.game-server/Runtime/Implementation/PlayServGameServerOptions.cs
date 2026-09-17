using System;

namespace Playserv.GameServer
{
    /// <summary>Configuration for the process-wide Unity Dedicated Server client.</summary>
    public sealed class PlayServGameServerOptions
    {
        /// <summary>Explicit inbound RPC handlers. Configure snapshots the registry; null advertises no rpc capability.</summary>
        public PlayServServerRpcRegistry RpcRegistry { get; set; }

        /// <summary>Runtime HTTP endpoint. Falls back to <c>PLAYSERV_API_URL</c>.</summary>
        public string BackendServerAddress { get; set; }

        /// <summary>Optional rotating key provider. Falls back to <c>PLAYSERV_SERVER_KEY</c>.</summary>
        public IPlayServServerKeyProvider ServerKeyProvider { get; set; }

        /// <summary>Open the process uplink before managed room registration. False preserves REST-only hosting without a roster.</summary>
        public bool EnableUplink { get; set; } = true;
        /// <summary>Advertises pushed tickets by default. Set false to retain ConsumeReservationAsync admission.</summary>
        public bool EnablePushedAdmission { get; set; } = true;
        /// <summary>Optional admission policy dispatched on Unity's context. Null accepts valid offers; budget is three seconds.</summary>
        public PlayServTicketOfferHandler TicketOfferHandler { get; set; }
        /// <summary>Maximum retained ticket entries, including replay guards, per process.</summary>
        public int AdmissionEntryLimit { get; set; } = 4096;
        /// <summary>Maximum retained serialized ticket bytes per process.</summary>
        public int AdmissionByteLimit { get; set; } = 4 * 1024 * 1024;
        /// <summary>Declared game-server executor. Falls back to PLAYSERV_EXECUTOR_SLUG; StartRoomAsync can supply it.</summary>
        public string ExecutorSlug { get; set; }
        /// <summary>Opaque process identity, 1–64 protocol-safe characters. Defaults to a process-stable random GUID.</summary>
        public string InstanceId { get; set; }
        /// <summary>Optional upgrade-only credential source. Otherwise uses PLAYSERV_DEPLOYMENT_TOKEN, then ServerKeyProvider.</summary>
        public IPlayServUplinkCredentialProvider UplinkCredentialProvider { get; set; }
        /// <summary>Optional game-local factory. Enables the room_create capability; never register from inside this callback.</summary>
        public PlayServRoomFactory RoomFactory { get; set; }
        /// <summary>Total room-create response/registration budget. Must match the platform's setting; default five seconds.</summary>
        public TimeSpan RoomCreateTimeout { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>Room heartbeat interval in the inclusive range of one to ten seconds.</summary>
        public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>Default request timeout. Matchmaking long polls derive their own deadline.</summary>
        public TimeSpan HttpTimeout { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>Maximum queued structured logs, including an in-flight entry. No global Unity log interception.</summary>
        public int LogQueueCapacity { get; set; } = 256;
        /// <summary>Maximum retained UTF-8 log frame bytes. A single frame must also fit the 1 MiB uplink limit.</summary>
        public int LogQueueMaxBytes { get; set; } = 1024 * 1024;
    }
}
