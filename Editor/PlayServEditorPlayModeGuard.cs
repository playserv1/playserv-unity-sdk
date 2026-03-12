#if UNITY_EDITOR
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
            if (state != PlayModeStateChange.ExitingPlayMode &&
                state != PlayModeStateChange.EnteredEditMode)
            {
                return;
            }

            try
            {
                PlayServ.Disconnect();
            }
            catch
            {
                // Best-effort cleanup on play mode transition.
            }
        }
    }
}
#endif
