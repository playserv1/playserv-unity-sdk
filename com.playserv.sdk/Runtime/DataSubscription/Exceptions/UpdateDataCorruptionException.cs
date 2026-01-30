using System;

namespace Playserv.DataSubscription.Exceptions
{
    public sealed class UpdateDataCorruptionException : DataSubscriptionException
    {
        public const int Code = 40001;

        public UpdateDataCorruptionException(string message)
            : base(Code, message ?? "Patch failed to apply - data corruption detected")
        {
        }
    }
}
