using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using Playserv.Proxy.Common;
using Playserv.Proxy.Logging;

namespace Playserv.Proxy.Implementation
{
#if !UNITY_WEBGL || UNITY_EDITOR
    /// <summary>
    /// UDP datagram transport for backend endpoints exposed via <c>udp://host:port</c>.
    /// Uses the same raw JSON frame contract as other PlayServ transports.
    /// </summary>
    internal sealed class UdpTransportImplementation : DatagramTransportBase
    {
        private const string UdpScheme = "udp";

        internal UdpTransportImplementation(string uri, ILogger logger = null)
            : base(uri, UdpScheme, "UDP", logger)
        {
        }

        public override async Task Send(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            var client = GetConnectedClient();

            try
            {
                await client.SendAsync(data, data.Length);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to send data via UDP transport: {ex.Message}");
                throw;
            }
        }

        protected override Task ProcessReceivedDatagramAsync(
            UdpClient client,
            byte[] buffer,
            ObservableByteChannel channel)
        {
            if (buffer == null || buffer.Length == 0)
            {
                Logger.LogWarning("Received empty UDP datagram.");
                return Task.CompletedTask;
            }

            channel.Next(buffer);
            return Task.CompletedTask;
        }
    }
#endif
}
