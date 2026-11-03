namespace Playserv.Editor
{
    internal sealed class PlayServDeploymentWindowBridge : IPlayServEditorSection
    {
        private readonly PlayServDeploymentSectionPresenter _presenter = new PlayServDeploymentSectionPresenter();
        public void Draw(PlayServWindowContext context) => _presenter.Draw(context);
        public void Dispose() => _presenter.Dispose();
    }
}
