namespace Playserv.Modules
{
    public static class PlayServModuleIds
    {
        public const string Core = "core";
        public const string Transport = "transport";
        public const string Serialization = "serialization";
        public const string Events = PlayServModuleManifest.EventsId;
        public const string Data = PlayServModuleManifest.DataSubscriptionId;
        public const string RpcCore = PlayServModuleManifest.RpcCoreId;
        public const string ClientRpc = PlayServModuleManifest.ClientRpcId;
        public const string ServerRpc = PlayServModuleManifest.ServerRpcId;
        public const string Rpc = ClientRpc;
        public const string Spawn = PlayServModuleManifest.SpawnId;
        public const string Pulse = PlayServModuleManifest.PulseId;
    }
}
