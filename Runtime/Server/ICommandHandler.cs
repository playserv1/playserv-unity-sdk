using System;

namespace Playserv.Server
{
    /// <summary>
    /// Contract for local command handling used by PlayServ.Send.
    /// </summary>
    public interface ICommandHandler
    {
        /// <summary>
        /// Tries to handle outgoing command in-process.
        /// </summary>
        /// <param name="command">Command payload.</param>
        /// <param name="moduleName">Target module name, can be null.</param>
        /// <returns>True when command was handled locally; otherwise false.</returns>
        bool TryHandle(object command, string moduleName);
    }
}
