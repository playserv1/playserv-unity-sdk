using System;
using System.Threading;
using System.Threading.Tasks;

namespace Playserv.GameServer
{
    /// <summary>Resolves the current <c>sk_*</c> credential for one server request.</summary>
    public interface IPlayServServerKeyProvider
    {
        Task<string> GetServerKeyAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>Adapts an asynchronous secret-store callback to a server-key provider.</summary>
    public sealed class PlayServDelegateServerKeyProvider : IPlayServServerKeyProvider
    {
        private readonly Func<CancellationToken, Task<string>> _resolver;

        public PlayServDelegateServerKeyProvider(Func<CancellationToken, Task<string>> resolver)
        {
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        }

        public Task<string> GetServerKeyAsync(CancellationToken cancellationToken = default)
        {
            return _resolver(cancellationToken);
        }
    }
}
