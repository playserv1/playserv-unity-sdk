using System;

namespace Playserv.DataSubscription
{
    /// <summary>
    /// Update payload for active data subscription.
    /// </summary>
    [Serializable]
    public sealed class DataSubscriptionUpdate
    {
        /// <summary>
        /// Request id correlated with originating request.
        /// </summary>
        public long RequestId { get; set; }

        /// <summary>
        /// Active subscription id.
        /// </summary>
        public long DataSubscriptionId { get; set; }

        /// <summary>
        /// Update type string ("Overwrite" or "Patch").
        /// </summary>
        public string UpdateType { get; set; }

        /// <summary>
        /// Indicates whether payload represents collection state.
        /// </summary>
        public bool IsCollection { get; set; }

        /// <summary>
        /// Update payload data (full value or patch operations).
        /// </summary>
        public object Data { get; set; }

        /// <summary>
        /// Optional error code from server.
        /// </summary>
        public int? ErrorCode { get; set; }

        /// <summary>
        /// Optional error message from server.
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>Backend error text used by current dataflow termination frames.</summary>
        public string Message { get; set; }

        public string EffectiveErrorMessage =>
            string.IsNullOrWhiteSpace(ErrorMessage) ? Message : ErrorMessage;

        /// <summary>Entity schema name included by backend reconnect restoration frames.</summary>
        public string EntityType { get; set; }

        /// <summary>Record key included by backend reconnect restoration frames.</summary>
        public string IdValue { get; set; }

        /// <summary>
        /// True when update represents error instead of data payload.
        /// </summary>
        public bool HasError => ErrorCode.HasValue;

        /// <summary>
        /// True when update type is overwrite.
        /// </summary>
        public bool IsOverwrite =>
            string.Equals(UpdateType, "Overwrite", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// True when update type is patch.
        /// </summary>
        public bool IsPatch =>
            string.Equals(UpdateType, "Patch", StringComparison.OrdinalIgnoreCase);
    }
}
