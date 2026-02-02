using System;

namespace Playserv.Proxy.Common
{
    [Serializable]
    public class SchemaRequest
    {
        public int RequestId;
        public string[] Query;
    }
}