using System;
using System.Net.Http;

namespace Playserv.Editor
{
    internal sealed class PlatformFunctionConnection : IDisposable
    {
        private string _api, _key;
        public PlatformFunctionClient Client { get; private set; }
        public bool Matches(string api, string key) => Client != null && _api == api && _key == key;
        internal void Create(DeploymentTarget target)
        {
            Dispose();
            Client = target.CreateClient();
            _api = target.Api; _key = target.Key;
        }
        public void Create(string api, string key, HttpClient http = null)
        {
            Dispose();
            Client = new PlatformFunctionClient(api, key, http);
            _api = api; _key = key;
        }
        public bool InvalidateIfChanged(string api, string key)
        {
            if (Client == null || Matches(api, key)) return false;
            Dispose(); return true;
        }
        internal static string ResolveKey(string environmentKey, string localKey) =>
            !string.IsNullOrWhiteSpace(environmentKey) ? environmentKey.Trim() : (localKey ?? "").Trim();
        public void Dispose() { Client?.Dispose(); Client = null; _api = null; _key = null; }
    }
}
