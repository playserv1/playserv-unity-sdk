using System;

namespace Playserv.Test.RPC
{
    [Serializable]
    public class RpcTestRequest
    {
        public string ServiceName { get; set; }
        public string MethodName { get; set; }
        
        public string Payload { get; set; }
    }
}