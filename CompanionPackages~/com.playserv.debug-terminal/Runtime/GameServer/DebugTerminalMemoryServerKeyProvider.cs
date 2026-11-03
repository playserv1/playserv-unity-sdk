using System;
using System.Threading;
using System.Threading.Tasks;
using Playserv.GameServer;

namespace Playserv.DebugTerminal.GameServer
{
    internal sealed class DebugTerminalMemoryServerKeyProvider : IPlayServServerKeyProvider, IDisposable
    {
        private readonly object _sync = new object();
        private string _serverKey;

        internal DebugTerminalMemoryServerKeyProvider(string serverKey)
        {
            _serverKey = serverKey ?? throw new ArgumentNullException(nameof(serverKey));
        }

        public Task<string> GetServerKeyAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync)
            {
                if (_serverKey == null)
                    throw new ObjectDisposedException(nameof(DebugTerminalMemoryServerKeyProvider));
                return Task.FromResult(_serverKey);
            }
        }

        public void Dispose()
        {
            lock (_sync)
                _serverKey = null;
        }
    }
}
