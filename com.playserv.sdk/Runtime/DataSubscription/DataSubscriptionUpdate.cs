using System;

namespace Playserv.DataSubscription
{
    [Serializable]
    public sealed class DataSubscriptionUpdate
    {
        public long RequestId { get; set; }
        public long DataSubscriptionId { get; set; }
        public string UpdateType { get; set; }
        public bool IsCollection { get; set; }
        public object Data { get; set; }
        public int? ErrorCode { get; set; }
        public string ErrorMessage { get; set; }

        public bool HasError => ErrorCode.HasValue;

        public bool IsOverwrite =>
            string.Equals(UpdateType, "Overwrite", StringComparison.OrdinalIgnoreCase);

        public bool IsPatch =>
            string.Equals(UpdateType, "Patch", StringComparison.OrdinalIgnoreCase);
    }
}
