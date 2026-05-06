#if !PLAYSERV_MODULE_DISABLED_LOCAL_EXECUTION_CORE
using System;
using Playserv.Server;

namespace Playserv.Wrapper
{
    internal static partial class PlayServCommandDispatchFactory
    {
        static partial void TryCreateModuleDispatch(ref IPlayServCommandDispatch dispatch)
        {
            var localExecution = CreateLocalExecution();
            if (localExecution != null)
                dispatch = new PlayServLocalExecutionCommandDispatch(localExecution);
        }

        private static IPlayServLocalExecution CreateLocalExecution()
        {
#if !PLAYSERV_MODULE_DISABLED_LOCAL_EXECUTION_SERVER
            return new PlayServServerLocalExecution();
#elif !PLAYSERV_MODULE_DISABLED_CLIENT_EXECUTION
            return new NoOpPlayServLocalExecution();
#else
            return null;
#endif
        }
    }

    internal sealed class PlayServLocalExecutionCommandDispatch : IPlayServCommandDispatch
    {
        private readonly IPlayServLocalExecution _localExecution;

        public PlayServLocalExecutionCommandDispatch(IPlayServLocalExecution localExecution)
        {
            _localExecution = localExecution ?? throw new ArgumentNullException(nameof(localExecution));
        }

        public object LocalExecution => _localExecution;

        public bool TryHandleCommand<T>(T command, string moduleName, bool hasRuntimeInstance)
        {
            return _localExecution.TryHandleCommand(command, moduleName, hasRuntimeInstance);
        }
    }
}
#endif
