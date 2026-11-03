using System.Collections.Generic;

namespace Playserv.Deploy.Editor.Analysis
{
    public sealed class AnalysisResult
    {
        public bool Success { get; set; }

        public List<string> Errors { get; set; } = new List<string>();

        public List<string> FilesToCompile { get; set; } = new List<string>();

        public List<RpcServiceMetadata> RpcServices { get; set; } = new List<RpcServiceMetadata>();
    }
}
