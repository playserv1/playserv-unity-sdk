using System;
namespace Playserv.Editor
{
    internal sealed class ServerImageDraft
    {
        private string _savedApi, _savedKey;
        internal string Api, Key, Server;
        internal string[] Servers { get; private set; }
        internal ServerImageDraft(string api, string key, string server)
        { Api = _savedApi = api; Key = _savedKey = key; Server = server; }
        internal void RefreshCredentials(string api, string key)
        {
            if (Api == _savedApi) Api = api;
            if (Key == _savedKey) Key = key;
            _savedApi = api; _savedKey = key;
        }
        internal void BeginConnection() { Servers = null; }
        internal void CompleteConnection(string[] servers)
        {
            Servers = servers;
            if (!Array.Exists(servers, slug => slug == Server)) Server = "";
        }
        internal void SelectServer(int index)
        {
            if (Servers == null) return;
            Server = index > 0 && index <= Servers.Length ? Servers[index - 1] : "";
        }
    }
}
