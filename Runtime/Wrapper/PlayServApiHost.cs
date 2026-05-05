namespace Playserv.Wrapper
{
    internal static class PlayServApiHost
    {
        private static readonly PlayServApi Api = new PlayServApi();

        internal static IPlayServConnectionApi Connection => Api;

#if !PLAYSERV_DISABLE_RPC_CORE && !PLAYSERV_DISABLE_CLIENT_RPC
        internal static IPlayServRpcApi Rpc => Api;
#endif

#if !PLAYSERV_DISABLE_RPC_CORE && !PLAYSERV_DISABLE_SERVER_RPC
        internal static IPlayServServerRpcApi ServerRpc => Api;
#endif

#if !PLAYSERV_DISABLE_EVENTS
        internal static IPlayServEventsApi Events => Api;
#endif

#if !PLAYSERV_DISABLE_DATA && !PLAYSERV_DISABLE_EVENTS
        internal static IPlayServDataApi Data => Api;
#endif

#if UNITY_5_3_OR_NEWER && !PLAYSERV_DISABLE_SPAWN && !PLAYSERV_DISABLE_EVENTS
        internal static IPlayServSpawnApi Spawn => Api;
#endif
    }
}
