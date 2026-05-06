#if !PLAYSERV_DISABLE_LOCAL_EXECUTION_CORE && !PLAYSERV_DISABLE_LOCAL_EXECUTION_SERVER
namespace Playserv.Server
{
    internal static partial class PlayServLocalExecutionFactory
    {
        static partial void TryCreateServer(ref IPlayServLocalExecution localExecution)
        {
            localExecution = new PlayServServerLocalExecution();
        }
    }
}
#endif
