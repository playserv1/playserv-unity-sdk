using System;
using System.Threading.Tasks;

namespace Playserv.Proxy.Interfaces
{
    /// <summary>
    /// Low-level byte transport implementation (websocket, WebGL bridge, etc.).
    /// </summary>
    public interface ITransportImplementation : IDisposable
    {
        /// <summary>
        /// Opens underlying connection.
        /// </summary>
        /// <returns>True when connected.</returns>
        Task<bool> Connect();

        /// <summary>
        /// Sends raw payload bytes.
        /// </summary>
        /// <param name="data">Serialized payload bytes.</param>
        Task Send(byte[] data);

        /// <summary>
        /// Resets current connection state so the next Connect call starts with a fresh transport session.
        /// </summary>
        void ResetConnection();

        /// <summary>
        /// Subscribes to incoming raw payload bytes.
        /// </summary>
        /// <returns>Observable byte stream.</returns>
        IObservable<byte[]> OnReceive();
    }
}
