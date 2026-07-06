using System;
using UnityEditor;
using Playserv.Wrapper;

namespace Playserv.Editor
{
    internal sealed class PlayServWindowContext
    {
        private readonly Action _ensureConfig;
        private readonly Action _updateWindowTitle;
        private readonly Action _focusConfigAsset;
        private readonly Action _repaint;

        public PlayServWindowContext(
            EditorWindow window,
            PlayServWindowState state,
            PlayServConnectionController connectionController,
            string docsUrl,
            float styledFieldHeight,
            Action ensureConfig,
            Action updateWindowTitle,
            Action focusConfigAsset,
            Action repaint)
        {
            Window = window ?? throw new ArgumentNullException(nameof(window));
            State = state ?? throw new ArgumentNullException(nameof(state));
            ConnectionController = connectionController ?? throw new ArgumentNullException(nameof(connectionController));
            DocsUrl = docsUrl ?? string.Empty;
            StyledFieldHeight = styledFieldHeight;
            _ensureConfig = ensureConfig ?? throw new ArgumentNullException(nameof(ensureConfig));
            _updateWindowTitle = updateWindowTitle ?? throw new ArgumentNullException(nameof(updateWindowTitle));
            _focusConfigAsset = focusConfigAsset ?? throw new ArgumentNullException(nameof(focusConfigAsset));
            _repaint = repaint ?? throw new ArgumentNullException(nameof(repaint));
        }

        public EditorWindow Window { get; }

        public PlayServWindowState State { get; }

        public PlayServConnectionController ConnectionController { get; }

        public string DocsUrl { get; }

        public float StyledFieldHeight { get; }

        public PlayServConfig Config => State.Config;

        public SerializedObject SerializedObject => State.SerializedObject;

        public SerializedProperty GameAccessTokenProperty => State.GameAccessTokenProperty;

        public SerializedProperty GameIdProperty => State.GameIdProperty;

        public SerializedProperty GameVersionProperty => State.GameVersionProperty;

        public SerializedProperty SdkVersionProperty => State.SdkVersionProperty;

        public SerializedProperty AllowMultipleConnectionsProperty => State.AllowMultipleConnectionsProperty;

        public SerializedProperty DeployAuthTokenProperty => State.DeployAuthTokenProperty;

        public SerializedProperty DeployTimeoutSecondsProperty => State.DeployTimeoutSecondsProperty;

        public void EnsureConfig()
        {
            _ensureConfig();
        }

        public void UpdateWindowTitle()
        {
            _updateWindowTitle();
        }

        public void FocusConfigAsset()
        {
            _focusConfigAsset();
        }

        public void Repaint()
        {
            _repaint();
        }
    }
}
