using System;
using Playserv.Proxy.Common;

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
            var localExecution = PlayServLocalExecutionFactoryRegistry.Create() ??
                                 new NoOpPlayServLocalExecution();
            return new PlayServLocalExecutionCommandDispatch(localExecution);
        }
    }

    internal sealed class PlayServCommandDispatch : IPlayServCommandDispatch
    {
        public static readonly IPlayServCommandDispatch None = new PlayServCommandDispatch();

        public object LocalExecution => null;

        public bool TryHandleCommand<T>(T command, string moduleName, bool hasRuntimeInstance) => false;
    }

    internal sealed class PlayServLocalExecutionCommandDispatch : IPlayServCommandDispatch
    {
        private readonly ILocalCommandExecution _localExecution;

        public PlayServLocalExecutionCommandDispatch(ILocalCommandExecution localExecution)
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
