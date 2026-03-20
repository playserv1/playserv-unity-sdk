using System;
using System.Threading.Tasks;

namespace Playserv.Proxy.Interfaces
{
    /// <summary>
    /// High-level transport abstraction for command send/receive.
    /// </summary>
    public interface ITransport : IDisposable
    {
        /// <summary>
        /// Opens underlying transport connection.
        /// </summary>
        /// <returns>True when connection is established.</returns>
        Task<bool> Connect();

        /// <summary>
        /// Sends command payload.
        /// </summary>
        /// <typeparam name="T">Command payload type.</typeparam>
        /// <param name="command">Command object.</param>
        /// <param name="moduleName">Optional module path override.</param>
        Task Send<T>(T command, string moduleName = null);

        /// <summary>
        /// Resets current transport connection state so a following Connect call uses a fresh session.
        /// </summary>
        void ResetConnection();

        /// <summary>
        /// Subscribes to received commands by deserialized payload type.
        /// </summary>
        /// <typeparam name="T">Expected payload type.</typeparam>
        /// <returns>Observable command stream.</returns>
        IObservable<T> OnReceive<T>();

        /// <summary>
        /// Subscribes to received commands by raw command name.
        /// </summary>
        /// <param name="commandName">Command name in envelope.</param>
        /// <returns>Observable command stream.</returns>
        IObservable<object> OnReceive(string commandName);
    }
}
