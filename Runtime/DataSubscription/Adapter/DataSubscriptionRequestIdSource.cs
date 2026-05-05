#if !PLAYSERV_DISABLE_DATA && !PLAYSERV_DISABLE_EVENTS
using System.Threading;

namespace Playserv.DataSubscription
{
    internal sealed class DataSubscriptionRequestIdSource
    {
        private long _requestIdCounter;

        public long Next()
        {
            return Interlocked.Increment(ref _requestIdCounter);
        }
    }
}

#endif
