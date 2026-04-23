using System;
using System.Text;
using Playserv.Proxy.Interfaces;
using ISdkLogger = Playserv.Proxy.Logging.ILogger;

namespace Playserv.Proxy.Implementation
{
    internal sealed class TransportReceiver
    {
        private readonly IMessageSerializer _serializer;
        private readonly ISdkLogger _logger;
        private readonly TransportChannelRegistry _channelRegistry;

        public TransportReceiver(
            IMessageSerializer serializer,
            ISdkLogger logger,
            TransportChannelRegistry channelRegistry)
        {
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _channelRegistry = channelRegistry ?? throw new ArgumentNullException(nameof(channelRegistry));
        }

        public void OnRawNext(byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                _logger.LogWarning("Received empty transport frame.");
                return;
            }

            MessageEnvelope envelope;
            object command;
            var json = string.Empty;

            try
            {
                json = Encoding.UTF8.GetString(data);
                LogTransportJson($"Received JSON: {json}", json);
                envelope = MessageEnvelopeParser.Parse(json);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to parse/deserialize incoming frame. Message skipped: {ex.Message}");
                return;
            }

            try
            {
                command = _serializer.Deserialize(envelope);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    $"Failed to deserialize incoming frame for command '{envelope.Command}'. Message skipped: {ex.Message}. Payload: {envelope.Payload}");
                return;
            }

            if (command == null)
            {
                _logger.LogWarning($"Received message with null command. Envelope: {envelope.Command}");
                return;
            }

            ICommandChannel typeChannel = null;
            ICommandNameChannel nameChannel = null;

            _channelRegistry.TryGetChannels(command.GetType(), envelope.Command, out typeChannel, out nameChannel);

            var hasTypedSubscriber = typeChannel != null;
            var hasCommandSubscriber = nameChannel != null;

            if (IsInvokeRpcResponseCommand(envelope.Command))
            {
                _logger.Log(
                    $"[PlayServ][RPC] Incoming command '{envelope.Command}' parsed as '{command.GetType().Name}'. typedSubscriber={hasTypedSubscriber}, nameSubscriber={hasCommandSubscriber}");

                if (!hasTypedSubscriber && !hasCommandSubscriber)
                {
                    _logger.LogWarning(
                        "[PlayServ][RPC] InvokeRpcResponse received but no subscriber is registered. Response will be dropped.");
                }
            }

            try
            {
                typeChannel?.Next(command);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    $"Error delivering message '{envelope.Command}' to typed channel '{command.GetType().Name}': {ex.Message}");
            }

            try
            {
                nameChannel?.Next(command);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    $"Error delivering message '{envelope.Command}' to command-name channel: {ex.Message}");
            }
        }

        private static bool IsInvokeRpcResponseCommand(string commandName)
        {
            if (string.IsNullOrWhiteSpace(commandName))
                return false;

            if (string.Equals(commandName, "InvokeRpcResponse", StringComparison.OrdinalIgnoreCase))
                return true;

            return commandName.EndsWith(".InvokeRpcResponse", StringComparison.OrdinalIgnoreCase);
        }

        private void LogTransportJson(string message, string json)
        {
#if PlayServ_Logs
            _logger.Log(message);
#else
            if (IsHeartbeatTransportFrame(json))
                return;

            _logger.Log(message);
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
