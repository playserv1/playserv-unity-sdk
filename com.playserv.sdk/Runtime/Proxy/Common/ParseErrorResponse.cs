using System;

namespace Playserv.Proxy.Common
{
    [Serializable]
    public sealed class ParseErrorResponse
    {
        private string error;
        private string receivedJson;

        public string Error => error;
        public string ReceivedJson => receivedJson;
    }
}
