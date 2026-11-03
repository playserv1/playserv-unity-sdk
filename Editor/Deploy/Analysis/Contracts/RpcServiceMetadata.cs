using System.Collections.Generic;

namespace Playserv.Deploy.Editor.Analysis
{
    public sealed class RpcServiceMetadata
    {
        public RpcServiceMetadata(string serviceName, List<string> methods)
        {
            ServiceName = serviceName;
            Methods = methods ?? new List<string>();
        }

        public string ServiceName { get; }

        public List<string> Methods { get; }
    }
}
