namespace Playserv.DataSubscription.Exceptions
{
    /// <summary>
    /// Indicates that patch/update payload could not be applied consistently.
    /// </summary>
    public sealed class UpdateDataCorruptionException : DataSubscriptionException
    {
        /// <summary>
        /// Error code used for data corruption/patch failure.
        /// </summary>
        public const int Code = 40001;

        /// <summary>
        /// Creates update data corruption exception.
        /// </summary>
        /// <param name="message">Optional custom message.</param>
        public UpdateDataCorruptionException(string message)
            : base(Code, message ?? "Patch failed to apply - data corruption detected")
        {
        }
    }
}
