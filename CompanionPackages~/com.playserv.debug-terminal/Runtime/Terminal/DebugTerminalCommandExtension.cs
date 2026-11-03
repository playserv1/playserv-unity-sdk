using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Playserv.DebugTerminal
{
    /// <summary>Internal bridge for optional terminal command assemblies.</summary>
    internal interface IDebugTerminalCommandExtension : IDisposable
    {
        Task ExecuteServerCommandAsync(IReadOnlyList<string> parts);

        Task ExecuteRecordCallerCommandAsync(IReadOnlyList<string> parts);
    }

    internal static class DebugTerminalCommandExtensionRegistry
    {
        private static readonly object Sync = new object();
        private static Func<PlayServDebugTerminal, IDebugTerminalCommandExtension> _factory;

        internal static void Register(
            Func<PlayServDebugTerminal, IDebugTerminalCommandExtension> factory)
        {
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));

            lock (Sync)
                _factory = factory;
        }

        internal static bool TryCreate(
            PlayServDebugTerminal terminal,
            out IDebugTerminalCommandExtension extension)
        {
            Func<PlayServDebugTerminal, IDebugTerminalCommandExtension> factory;
            lock (Sync)
                factory = _factory;

            extension = factory?.Invoke(terminal);
            return extension != null;
        }
    }
}
