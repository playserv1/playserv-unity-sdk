namespace Playserv.Wrapper
{
    internal interface IPlayServCommandDispatch
    {
        object LocalExecution { get; }

        bool TryHandleCommand<T>(T command, string moduleName, bool hasRuntimeInstance);
    }

    internal static partial class PlayServCommandDispatchFactory
    {
        public static IPlayServCommandDispatch Create()
        {
            IPlayServCommandDispatch dispatch = null;
            TryCreateModuleDispatch(ref dispatch);
            return dispatch ?? PlayServCommandDispatch.None;
        }

        static partial void TryCreateModuleDispatch(ref IPlayServCommandDispatch dispatch);
    }

    internal sealed class PlayServCommandDispatch : IPlayServCommandDispatch
    {
        public static readonly IPlayServCommandDispatch None = new PlayServCommandDispatch();

        public object LocalExecution => null;

        public bool TryHandleCommand<T>(T command, string moduleName, bool hasRuntimeInstance) => false;
    }
}
