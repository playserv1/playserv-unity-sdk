namespace Playserv.DataSubscription
{
    /// <summary>
    /// Type of data synchronization update.
    /// </summary>
    public enum UpdateType
    {
        /// <summary>
        /// Full value replacement.
        /// </summary>
        Overwrite,

        /// <summary>
        /// Incremental patch update.
        /// </summary>
        Patch
    }
}
