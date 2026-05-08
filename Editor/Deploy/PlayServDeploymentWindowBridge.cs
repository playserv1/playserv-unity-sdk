using System;
using Playserv.Deploy.Editor;

namespace Playserv.Editor
{
    internal sealed class PlayServDeploymentWindowBridge : IPlayServEditorSection
    {
        private readonly DeploymentClosureFilter _closureFilter = new DeploymentClosureFilter();
        private readonly DeploymentZipBuilder _zipBuilder = new DeploymentZipBuilder();
        private readonly PlayServDeploymentSectionPresenter _presenter;

        private DeploymentUploadAction _uploadAction;
        private VersionSyncAction _versionSyncAction;
        private PlayServDeploymentController _controller;

        public PlayServDeploymentWindowBridge()
        {
            _presenter = new PlayServDeploymentSectionPresenter(GetController);
        }

        public void Draw(PlayServWindowContext context)
        {
            EnsureController(context);
            _presenter.Draw(context);
        }

        public void Dispose()
        {
            _controller?.Dispose();
            _controller = null;
        }

        private PlayServDeploymentController GetController(PlayServWindowContext context)
        {
            EnsureController(context);
            return _controller;
        }

        private void EnsureController(PlayServWindowContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            if (_uploadAction == null)
                _uploadAction = new DeploymentUploadAction(_zipBuilder);

            if (_versionSyncAction == null)
                _versionSyncAction = new VersionSyncAction(_zipBuilder);

            if (_controller == null)
            {
                _controller = new PlayServDeploymentController(
                    context.State,
                    _closureFilter,
                    _uploadAction,
                    _versionSyncAction,
                    context.Repaint);
            }
        }
    }
}
