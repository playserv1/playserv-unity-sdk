namespace Playserv.Modules
{
    public static class PlayServModuleIds
    {
        public const string Core = "core";
        public const string Transport = "transport";
        public const string Serialization = "serialization";
        public const string ClientExecution = PlayServModuleManifest.ClientExecutionId;
        public const string Events = PlayServModuleManifest.EventsId;
        public const string Data = PlayServModuleManifest.DataSubscriptionId;
        public const string RpcCore = PlayServModuleManifest.RpcCoreId;
        public const string ClientRpc = PlayServModuleManifest.ClientRpcId;
        public const string Server = PlayServModuleManifest.ServerId;
        public const string Rpc = ClientRpc;
        public const string Spawn = PlayServModuleManifest.SpawnId;
        public const string Pulse = PlayServModuleManifest.PulseId;
        public const string Analytics = PlayServModuleManifest.AnalyticsId;
        public const string DebugTerminal = PlayServModuleManifest.DebugTerminalId;
        public const string AppleSignIn = PlayServModuleManifest.AppleSignInId;
        public const string GoogleSignIn = PlayServModuleManifest.GoogleSignInId;
        public const string FacebookLogin = PlayServModuleManifest.FacebookLoginId;
        public const string EpicAuth = PlayServModuleManifest.EpicAuthId;
        public const string SteamAuth = PlayServModuleManifest.SteamAuthId;
        public const string GameServer = PlayServModuleManifest.GameServerId;
        public const string TransportWebSocket = PlayServModuleManifest.TransportWebSocketId;
        public const string TransportUdp = PlayServModuleManifest.TransportUdpId;
        public const string TransportRudp = PlayServModuleManifest.TransportRudpId;
        public const string TransportWebRtc = PlayServModuleManifest.TransportWebRtcId;
    }
}
