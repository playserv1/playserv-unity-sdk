namespace Playserv.Wrapper
{
    internal static class PlayServApiHost
    {
        private static readonly PlayServApi Api = new PlayServApi();

        internal static IPlayServConnectionApi Connection => Api;
    }
}
