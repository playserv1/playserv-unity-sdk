#if !PLAYSERV_DISABLE_LOCAL_EXECUTION_CORE
namespace Playserv.Server
{
    internal static partial class PlayServLocalExecutionFactory
    {
        public static IPlayServLocalExecution Create()
        {
            IPlayServLocalExecution localExecution = null;
            TryCreateServer(ref localExecution);
#if PLAYSERV_DISABLE_CLIENT_EXECUTION
            if (localExecution == null)
            {
                throw new System.InvalidOperationException(
                    "Local execution core is enabled, but neither client nor server local execution implementation is available.");
            }

            return localExecution;
#else
            return localExecution ?? new NoOpPlayServLocalExecution();
#endif
        }

        static partial void TryCreateServer(ref IPlayServLocalExecution localExecution);
    }
}
#endif
