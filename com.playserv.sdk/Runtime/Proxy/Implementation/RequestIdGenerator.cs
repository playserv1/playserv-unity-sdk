using System.Threading;
using Playserv.Proxy.Interfaces;

namespace Playserv.Proxy.Implementation
{
    public sealed class RequestIdGenerator : IRequestIdGenerator
    {
        private int _counter;

        public int Next() => Interlocked.Increment(ref _counter);
    }
}