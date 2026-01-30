using System;

namespace Playserv.DataSubscription.JsonPatch
{
    [Serializable]
    public sealed class PatchOperation
    {
        public string Op { get; set; }
        public string Path { get; set; }
        public object Value { get; set; }
        public string From { get; set; }
    }
}
