using System;

namespace Playserv.DataSubscription.Responses
{
    [Serializable]
    public sealed class DataMutationResponse
    {
        public long RequestId { get; set; }
        public DataMutationResult Result { get; set; }
    }

    [Serializable]
    public sealed class DataMutationResult
    {
        public bool Success { get; set; }
        public DataMutationDetails Details { get; set; }
        public DataMutationError Error { get; set; }
    }

    [Serializable]
    public sealed class DataMutationDetails
    {
        // OldValues comes as an arbitrary JSON object
        public object OldValues { get; set; }
    }

    [Serializable]
    public sealed class DataMutationError
    {
        public int Code { get; set; }
        public string Message { get; set; }
        public object Payload { get; set; }
    }
}

