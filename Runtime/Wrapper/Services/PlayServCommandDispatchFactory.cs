using System;
#if !PLAYSERV_DISABLE_LOCAL_EXECUTION_CORE
using Playserv.Server;
#endif

namespace Playserv.Wrapper
{
    internal interface IPlayServCommandDispatch
    {
        object LocalExecution { get; }

        bool TryHandleCommand<T>(T command, string moduleName, bool hasRuntimeInstance);
    }

    internal static class PlayServCommandDispatchFactory
    {
        public static IPlayServCommandDispatch Create()
        {
#if !PLAYSERV_DISABLE_LOCAL_EXECUTION_CORE
            var localExecution = CreateLocalExecution();
            return localExecution == null
                ? PlayServCommandDispatch.None
                : new PlayServLocalExecutionCommandDispatch(localExecution);
#else
            return PlayServCommandDispatch.None;
#endif
        }

#if !PLAYSERV_DISABLE_LOCAL_EXECUTION_CORE
        private static IPlayServLocalExecution CreateLocalExecution()
        {
#if !PLAYSERV_DISABLE_LOCAL_EXECUTION_SERVER
            return new PlayServServerLocalExecution();
#elif !PLAYSERV_DISABLE_CLIENT_EXECUTION
            return new NoOpPlayServLocalExecution();
#else
            return null;
#endif
        }
#endif
    }

    internal sealed class PlayServCommandDispatch : IPlayServCommandDispatch
    {
        public static readonly IPlayServCommandDispatch None = new PlayServCommandDispatch();

        public object LocalExecution => null;

        public bool TryHandleCommand<T>(T command, string moduleName, bool hasRuntimeInstance) => false;
    }

#if !PLAYSERV_DISABLE_LOCAL_EXECUTION_CORE
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
#endif
}
