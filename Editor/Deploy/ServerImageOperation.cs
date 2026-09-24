using System;
using System.Threading;
using System.Threading.Tasks;
namespace Playserv.Editor
{
    internal sealed class ServerImageOperation : IDisposable
    {
        private CancellationTokenSource _active;
        private int _generation;
        private bool _disposed;
        internal bool Running => _active != null;
        internal bool Cancelled => _disposed || _active == null || _active.IsCancellationRequested;
        internal async Task<bool> TryRunAsync(Func<CancellationToken, Task<Action>> action, Action<Exception> failure)
        {
            if (_disposed || Running) return false;
            var generation = ++_generation;
            var cancellation = new CancellationTokenSource();
            _active = cancellation;
            try
            {
                var apply = await action(cancellation.Token);
                if (!_disposed && generation == _generation && !cancellation.IsCancellationRequested) apply?.Invoke();
            }
            catch (Exception error)
            {
                if (!_disposed && generation == _generation && !cancellation.IsCancellationRequested) failure(error);
            }
            finally
            {
                if (ReferenceEquals(_active, cancellation)) _active = null;
                cancellation.Dispose();
            }
            return true;
        }
        internal void Cancel() { _generation++; _active?.Cancel(); }
        public void Dispose() { _disposed = true; Cancel(); }
    }
}
