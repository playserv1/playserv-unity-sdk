using System;
using UnityEditor;
using UnityEngine;

namespace Playserv.Editor
{
    internal sealed class PlayServDeploymentSectionPresenter : IDisposable
    {
        private readonly PlatformFunctionPanel _platformFunctions = new PlatformFunctionPanel();
        private readonly ServerImagePanel _serverImages = new ServerImagePanel();
        internal static readonly string[] TabLabels = { "Platform Functions", "Server Images" };
        private int _mode;
        private int _drawMode;

        public void Dispose() { _platformFunctions.Dispose(); _serverImages.Dispose(); }

        public void Draw(PlayServWindowContext context)
        {
            _platformFunctions.Config = () => context.Config;
            _serverImages.Config = () => context.Config;
            if (Event.current.type == EventType.Layout) _drawMode = _mode;
            var expanded = PlayServWindowChrome.BeginSectionCard(
                ref context.State.FoldDeployment, "Server Code", "Deployment",
                "Deploy platform functions or publish a game-server image to your project.");
            if (expanded)
            {
                using (new EditorGUI.DisabledScope(_platformFunctions.Running || _serverImages.Running))
                    _mode = GUILayout.Toolbar(_mode, TabLabels);
                if (_drawMode == 0) _platformFunctions.Draw(context.Repaint);
                else _serverImages.Draw(context.Repaint);
            }
            PlayServWindowChrome.EndSectionCard(expanded);
            EditorPrefs.SetBool(Const.PrefFoldDeployment, context.State.FoldDeployment);
        }
    }
}
