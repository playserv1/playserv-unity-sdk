using System;

namespace Playserv.Proxy.Common
{
    [Serializable]
    public sealed class ValidationErrorResponse
    {
        private string error;
        private string receivedJson;

        public string Error => error;
        public string ReceivedJson => receivedJson;
    }
}