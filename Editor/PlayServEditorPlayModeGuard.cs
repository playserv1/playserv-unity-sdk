using Playserv.Wrapper;
using System.Reflection;
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
                var runtimeType = typeof(PlayServ).Assembly.GetType("Playserv.Wrapper.PlayServRuntimeShutdownState");
                if (runtimeType == null)
                    return;

                var methodName = isShuttingDown ? "MarkShuttingDown" : "Reset";
                var method = runtimeType.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic);
                method?.Invoke(null, null);
            }
            catch
            {
                // Best-effort editor shutdown bridge.
            }
        }
    }
}
