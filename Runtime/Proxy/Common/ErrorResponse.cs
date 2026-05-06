using System;

namespace Playserv.Proxy.Common
{
    [Serializable]
    public class ErrorResponse
    {
        public int ErrorCode;
        public string Message;
    }
}
