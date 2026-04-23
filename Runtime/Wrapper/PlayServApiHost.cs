namespace Playserv.Wrapper
{
    internal static class PlayServApiHost
    {
        private static readonly PlayServApi Api = new PlayServApi();

        internal static IPlayServConnectionApi Connection => Api;

        internal static IPlayServRpcApi Rpc => Api;

        internal static IPlayServEventsApi Events => Api;

        internal static IPlayServDataApi Data => Api;

#if UNITY_5_3_OR_NEWER
        internal static IPlayServSpawnApi Spawn => Api;
#endif
    }
}
