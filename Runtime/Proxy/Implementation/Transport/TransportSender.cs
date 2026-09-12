using System;
using System.Text;
using System.Threading.Tasks;
using Playserv.Proxy.Interfaces;
using ISdkLogger = Playserv.Proxy.Logging.ILogger;

namespace Playserv.Proxy.Implementation
{
    internal sealed class TransportSender
    {
        private readonly ITransportImplementation _implementation;
        private readonly IMessageSerializer _serializer;
        private readonly MessageEnvelopeCodec _envelopeCodec;
        private readonly ISdkLogger _logger;
        private readonly Func<bool> _isDisposed;
        private readonly TransportLogRedactor _logRedactor;
        private readonly System.Threading.SemaphoreSlim _sendGate = new System.Threading.SemaphoreSlim(1, 1);

        public TransportSender(
            ITransportImplementation implementation,
            IMessageSerializer serializer,
            MessageEnvelopeCodec envelopeCodec,
            ISdkLogger logger,
            Func<bool> isDisposed,
            TransportLogRedactor logRedactor)
        {
            _implementation = implementation ?? throw new ArgumentNullException(nameof(implementation));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _envelopeCodec = envelopeCodec ?? throw new ArgumentNullException(nameof(envelopeCodec));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _isDisposed = isDisposed ?? throw new ArgumentNullException(nameof(isDisposed));
            _logRedactor = logRedactor ?? throw new ArgumentNullException(nameof(logRedactor));
        }

        public async Task Send<T>(T command, string moduleName = null)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            if (_isDisposed())
            {
                _logger.LogWarning(
                    $"[Transport] Send skipped because transport is already disposed. Command={typeof(T).Name}");
                return;
            }

            try
            {
                await _sendGate.WaitAsync();
                try
                {
                    if (_isDisposed())
                    {
                        _logger.LogWarning(
                            $"[Transport] Send skipped after wait because transport is disposed. Command={typeof(T).Name}");
                        return;
                    }

                    var envelope = _serializer.Serialize(command, moduleName);
                    var json = _envelopeCodec.Serialize(envelope);
                    var data = Encoding.UTF8.GetBytes(json);

#if !PLAYSERV_DISABLE_LOGS
                    var packetName = OutboundPacketDiagnostics.Register(envelope, command, data);
                    _logger.LogWarning($"[PKT-OUT] {_logRedactor.RedactText(packetName)}");
#endif
                    await _implementation.Send(data);
                    LogTransportJson($"Message sent: {typeof(T).Name} with payload", json);
                }
                finally
                {
                    _sendGate.Release();
                }
            }
            catch (ObjectDisposedException) when (_isDisposed())
            {
                _logger.LogWarning(
                    $"[Transport] Late send ignored during teardown. Command={typeof(T).Name}");
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    $"Failed to send message {typeof(T).Name}: {_logRedactor.RedactText(ex.Message)}");
                throw;
            }
        }

        private void LogTransportJson(string description, string json)
        {
#if PlayServ_Logs
            _logger.Log($"{description}: {_logRedactor.RedactJson(json)}");
#else
            if (IsHeartbeatTransportFrame(json))
                return;

            _logger.Log($"{description}: {_logRedactor.RedactJson(json)}");
#endif
        }

        private static bool IsHeartbeatTransportFrame(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return false;

            return json.IndexOf("\"KeepAlive\"", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   json.IndexOf("\"Ping\"", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   json.IndexOf("\"Pong\"", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
