#if !PLAYSERV_DISABLE_DATA && !PLAYSERV_DISABLE_EVENTS
using System;

namespace Playserv.DataSubscription.JsonPatch
{
    /// <summary>
    /// Single RFC6902-like JSON patch operation.
    /// </summary>
    [Serializable]
    public sealed class PatchOperation
    {
        /// <summary>
        /// Operation name (add/remove/replace/move/copy/test).
        /// </summary>
        public string Op { get; set; }

        /// <summary>
        /// Target JSON pointer path.
        /// </summary>
        public string Path { get; set; }

        /// <summary>
        /// Operation value payload.
        /// </summary>
        public object Value { get; set; }

        /// <summary>
        /// Source path for move/copy operations.
        /// </summary>
        public string From { get; set; }
    }
}

#endif
