using System;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Playserv.GameServer
{
    // Socket and clock seams are internal: tests exercise the same framing and serializer as the host.
    internal interface IPlayServUplinkSocket : IDisposable
    {
        Task ConnectAsync(Uri endpoint, string credential, CancellationToken ct);
        Task SendAsync(byte[] bytes, CancellationToken ct);
        Task<string> ReceiveAsync(CancellationToken ct);
        void Abort();
    }

    internal sealed class PlayServUplinkFailure : Exception
    {
        internal readonly string Code;
        internal readonly bool Terminal;
        internal PlayServUplinkFailure(string code, bool terminal = false) : base("PlayServ uplink failed.")
        { Code = code; Terminal = terminal; }
    }

    internal sealed class PlayServUplinkSocket : IPlayServUplinkSocket
    {
        internal const int MaxFrameBytes = 1024 * 1024;
        private readonly WebSocket _socket;
        private readonly SemaphoreSlim _send = new SemaphoreSlim(1, 1);
        internal PlayServUplinkSocket() : this(new ClientWebSocket()) { }
        internal PlayServUplinkSocket(WebSocket socket) { _socket = socket; }

        public async Task ConnectAsync(Uri endpoint, string credential, CancellationToken ct)
        {
            var client = (ClientWebSocket)_socket;
            client.Options.SetRequestHeader("Authorization", "Bearer " + credential);
            try { await client.ConnectAsync(endpoint, ct).ConfigureAwait(false); }
            catch (WebSocketException ex)
            {
                // Older Unity profiles do not expose the upgrade status on ClientWebSocket.
                // Inspect only for classification; never preserve/log the exception or its text.
                var web = ex.InnerException as WebException;
                var response = web?.Response as HttpWebResponse;
                if (response?.StatusCode == HttpStatusCode.Unauthorized ||
                    (ex.Message != null && ex.Message.Contains("401")))
                    throw new PlayServUplinkFailure("uplink_unauthorized", true);
                throw new PlayServUplinkFailure("uplink_network_error");
            }
        }

        public async Task SendAsync(byte[] bytes, CancellationToken ct)
        {
            if (bytes.Length > MaxFrameBytes) throw new PlayServUplinkFailure("frame_too_large", true);
            await _send.WaitAsync(ct).ConfigureAwait(false);
            try { await _socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct).ConfigureAwait(false); }
            finally { _send.Release(); }
        }

        public async Task<string> ReceiveAsync(CancellationToken ct)
        {
            var buffer = new byte[8192];
            using var frame = new MemoryStream();
            while (true)
            {
                var read = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                if (read.MessageType == WebSocketMessageType.Close)
                    throw new PlayServUplinkFailure(SafeCloseReason(read.CloseStatusDescription),
                        read.CloseStatus == WebSocketCloseStatus.PolicyViolation);
                if (read.MessageType != WebSocketMessageType.Text)
                    throw new PlayServUplinkFailure("uplink_invalid_frame", true);
                if (frame.Length + read.Count > MaxFrameBytes)
                { _socket.Abort(); throw new PlayServUplinkFailure("frame_too_large", true); }
                frame.Write(buffer, 0, read.Count);
                if (read.EndOfMessage) return new UTF8Encoding(false, true).GetString(frame.ToArray());
            }
        }

        internal static string SafeCloseReason(string reason)
        {
            switch (reason)
            {
                case "instance_id_missing": case "instance_id_conflict": case "protocol_unsupported":
                case "executor_not_found": return reason;
                default: return "uplink_closed";
            }
        }
        public void Abort() => _socket.Abort();
        public void Dispose() => _socket.Dispose();
    }
}
