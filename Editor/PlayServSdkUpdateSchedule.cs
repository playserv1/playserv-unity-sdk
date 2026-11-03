using System.Threading.Tasks;

namespace Playserv.Editor
{
    internal sealed class PlayServSdkUpdateSchedule
    {
        private readonly PlayServSdkUpdateController _controller;
        private bool _windowOpen;
        private bool _checkQueued;
        private bool _recoveryQueued = true;
        private bool _retainResult;

        public PlayServSdkUpdateSchedule(PlayServSdkUpdateController controller)
        {
            _controller = controller;
            _controller.Changed += () =>
            {
                if (_controller.State == PlayServSdkUpdateState.Preparing ||
                    _controller.State == PlayServSdkUpdateState.Verifying)
                    _retainResult = true;
                else if (_controller.State == PlayServSdkUpdateState.Available ||
                         _controller.State == PlayServSdkUpdateState.Current)
                    _retainResult = false;
            };
        }

        public void WindowOpened() { _windowOpen = true; _checkQueued = true; }
        public void WindowClosed() => _windowOpen = false;
        public void PackagesChanged() { if (_windowOpen) _checkQueued = true; }
        public Task CheckAsync() { _retainResult = false; _checkQueued = false; return _controller.CheckAsync(true); }

        public async Task TickAsync(bool batchMode)
        {
            if (_controller.IsBusy) return;
            if (_recoveryQueued)
            {
                _recoveryQueued = false;
                await _controller.RecoverAsync();
                if (_controller.State == PlayServSdkUpdateState.Error || _controller.State == PlayServSdkUpdateState.Updated)
                    _retainResult = true;
                return;
            }
            if (!_windowOpen || !_checkQueued || batchMode || _retainResult) return;
            _checkQueued = false;
            await _controller.CheckAsync(false);
        }
    }
}
