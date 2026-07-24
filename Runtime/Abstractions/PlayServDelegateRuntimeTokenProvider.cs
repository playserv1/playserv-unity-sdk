using System;
using System.Threading;
using System.Threading.Tasks;

namespace Playserv.Runtime.Abstractions
{
    /// <summary>
    /// Adapts an asynchronous token callback to <see cref="IPlayServRuntimeTokenProvider"/>.
    /// </summary>
    public sealed class PlayServDelegateRuntimeTokenProvider : IPlayServRuntimeTokenProvider
    {
        private readonly Func<CancellationToken, Task<string>> _resolveToken;

        public PlayServDelegateRuntimeTokenProvider(Func<CancellationToken, Task<string>> resolveToken)
        {
            _resolveToken = resolveToken ?? throw new ArgumentNullException(nameof(resolveToken));
        }

        public Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
        {
            return _resolveToken(cancellationToken);
        }
    }
}
