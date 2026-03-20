using System;
using System.Collections.Generic;

namespace Playserv.Server
{
    /// <summary>
    /// In-process command handler with module-based routing.
    /// </summary>
    public sealed class LocalCommandHandler : ICommandHandler
    {
        private readonly Dictionary<string, Action<object>> _moduleHandlers = new(StringComparer.Ordinal);
        private Action<object, string> _fallbackHandler;

        /// <summary>
        /// Registers command handler for exact module name.
        /// </summary>
        /// <param name="moduleName">Module name.</param>
        /// <param name="handler">Command handler.</param>
        /// <returns>Current handler instance for chaining.</returns>
        public LocalCommandHandler RegisterModule(string moduleName, Action<object> handler)
        {
            if (string.IsNullOrWhiteSpace(moduleName))
                throw new ArgumentException("Module name is required.", nameof(moduleName));

            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            _moduleHandlers[moduleName] = handler;
            return this;
        }

        /// <summary>
        /// Registers fallback command handler for any module.
        /// </summary>
        /// <param name="handler">Fallback command handler.</param>
        /// <returns>Current handler instance for chaining.</returns>
        public LocalCommandHandler RegisterFallback(Action<object, string> handler)
        {
            _fallbackHandler = handler ?? throw new ArgumentNullException(nameof(handler));
            return this;
        }

        /// <summary>
        /// Clears all handlers.
        /// </summary>
        public void Clear()
        {
            _moduleHandlers.Clear();
            _fallbackHandler = null;
        }

        /// <summary>
        /// Tries to route command to module handler or fallback handler.
        /// </summary>
        /// <param name="command">Command payload.</param>
        /// <param name="moduleName">Module name.</param>
        /// <returns>True when command was handled; otherwise false.</returns>
        public bool TryHandle(object command, string moduleName)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            if (moduleName != null &&
                _moduleHandlers.TryGetValue(moduleName, out var moduleHandler))
            {
                moduleHandler(command);
                return true;
            }

            if (_fallbackHandler != null)
            {
                _fallbackHandler(command, moduleName);
                return true;
            }

            return false;
        }
    }
}
