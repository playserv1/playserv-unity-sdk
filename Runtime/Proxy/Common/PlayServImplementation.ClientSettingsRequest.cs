namespace Playserv.Proxy.Common
{
    public sealed partial class PlayServImplementation
    {
        [System.Serializable]
        private sealed class ClientSettingsRequest
        {
            public bool allowMultipleConnections;
        }
    }
}