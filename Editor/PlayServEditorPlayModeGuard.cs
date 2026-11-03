using Playserv.Wrapper;
using UnityEditor;

namespace Playserv.Editor
{
    [InitializeOnLoad]
    internal static class PlayServEditorPlayModeGuard
    {
        static PlayServEditorPlayModeGuard()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode ||
                state == PlayModeStateChange.EnteredPlayMode)
            {
                SetRuntimeShutdownState(false);
                return;
            }

            if (state != PlayModeStateChange.ExitingPlayMode &&
                state != PlayModeStateChange.EnteredEditMode)
            {
                return;
            }

            try
            {
                SetRuntimeShutdownState(true);
                PlayServ.Disconnect();
            }
            catch
            {
                // Best-effort cleanup on play mode transition.
            }
        }

        private static void SetRuntimeShutdownState(bool isShuttingDown)
        {
            try
            {
                if (isShuttingDown)
                    PlayServRuntimeEditorBridge.MarkShuttingDown();
                else
                    PlayServRuntimeEditorBridge.ResetShutdownState();
            }
            catch
            {
                // Best-effort editor shutdown bridge.
            }
        }
    }
}
