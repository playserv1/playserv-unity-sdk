namespace Playserv.Wrapper
{
    internal static class PlayServRuntimeEditorBridge
    {
        public static void MarkShuttingDown()
        {
            PlayServRuntimeShutdownState.MarkShuttingDown();
        }

        public static void ResetShutdownState()
        {
            PlayServRuntimeShutdownState.Reset();
        }
    }
}
