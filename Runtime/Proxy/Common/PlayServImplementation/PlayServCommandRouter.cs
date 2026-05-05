using System;
using System.Collections.Generic;
using Playserv.Proxy.Logging;
#if !PLAYSERV_DISABLE_RPC
using Playserv.RPC;
#endif

namespace Playserv.Proxy.Common
{
    internal sealed class PlayServCommandRouter : IDisposable
    {
        private readonly PlayServFeatureFacade _featureFacade;
        private readonly PlayServTransportSession _transportSession;
        private readonly ILogger _logger;
        private readonly List<IDisposable> _subscriptions = new List<IDisposable>();

        public PlayServCommandRouter(
            PlayServFeatureFacade featureFacade,
            PlayServTransportSession transportSession,
            ILogger logger)
        {
            _featureFacade = featureFacade ?? throw new ArgumentNullException(nameof(featureFacade));
            _transportSession = transportSession ?? throw new ArgumentNullException(nameof(transportSession));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            Attach();
        }

        public void Dispose()
        {
            for (var i = 0; i < _subscriptions.Count; i++)
                _subscriptions[i].Dispose();

            _subscriptions.Clear();
        }

        private void Attach()
        {
            Register("error", OnCommandErrorReceived);
            Register("CommandErrorResponse", OnCommandErrorReceived);
#if !PLAYSERV_DISABLE_RPC
            Register("RpcErrorResponse", OnCommandErrorReceived);
            Register("rpc.RpcErrorResponse", OnCommandErrorReceived);
#endif
            Register("Disconnect", OnDisconnectReceived);
            Register("ForcedDisconnect", OnForcedDisconnectReceived);
            Register("module_proxy.ForcedDisconnect", OnForcedDisconnectReceived);
            Register("ClientSettingsResponse", OnClientSettingsResponseReceived);
            Register("ParseErrorResponse", OnParseErrorReceived);
            Register("ValidationErrorResponse", OnValidationErrorReceived);
#if !PLAYSERV_DISABLE_RPC
            Register("InvokeRpcResponse", OnInvokeRpcResponseReceived);
            Register("rpc.InvokeRpcResponse", OnInvokeRpcResponseReceived);
            Register("rpc.InvokeRpc.InvokeRpcResponse", OnInvokeRpcResponseReceived);
#endif
        }

        private void Register(string commandName, Action<object> onNext)
        {
            _subscriptions.Add(_featureFacade.OnCommand(commandName, onNext));
        }

        private void OnDisconnectReceived(object command)
        {
            _transportSession.HandleDisconnect();
        }

        private void OnForcedDisconnectReceived(object command)
        {
            _transportSession.HandleForcedDisconnect(command as ForcedDisconnectResponse);
        }

        private void OnClientSettingsResponseReceived(object command)
        {
            var response = command as ClientSettingsResponse;
            if (response != null)
            {
                _transportSession.HandleClientSettingsResponse(response);
                return;
            }

            _logger.LogWarning($"Received ClientSettingsResponse with unexpected payload type: {command?.GetType().Name ?? "null"}");
        }

        private void OnParseErrorReceived(object command)
        {
            var response = command as ParseErrorResponse;
            if (response != null)
            {
                _logger.LogError($"Parse error received from server. Error: {response.Error}, Received JSON: {response.ReceivedJson}");
                return;
            }

            _logger.LogWarning($"Received ParseErrorResponse with unexpected payload type: {command?.GetType().Name ?? "null"}");
        }

        private void OnValidationErrorReceived(object command)
        {
            var response = command as ValidationErrorResponse;
            if (response != null)
            {
                _logger.LogError($"Validation error received from server. Error: {response.Error}, Received JSON: {response.ReceivedJson}");
                return;
            }

            _logger.LogWarning($"Received ValidationErrorResponse with unexpected payload type: {command?.GetType().Name ?? "null"}");
        }

        private void OnCommandErrorReceived(object command)
        {
            var response = command as CommandErrorResponse;
            if (response != null)
            {
                if (IsUnsupportedDataSubscriptionRefresh(response))
                {
                    _logger.LogWarning(
                        $"Server command warning received. Error: {response.Error}, Message: {response.Message}, Timestamp: {response.Timestamp}");
                    return;
                }

                _logger.LogError(
                    $"Server command error received. Error: {response.Error}, Message: {response.Message}, Timestamp: {response.Timestamp}");
                return;
            }

            _logger.LogWarning($"Received 'error' command with unexpected payload type: {command?.GetType().Name ?? "null"}");
        }

#if !PLAYSERV_DISABLE_RPC
        private void OnInvokeRpcResponseReceived(object command)
        {
            var response = command as InvokeRpcResponse;
            if (response != null)
            {
                _transportSession.HandleInvokeRpcResponse(response);
                return;
            }

            _logger.LogWarning($"[PlayServ][RPC] Received InvokeRpcResponse with unexpected payload type: {command?.GetType().Name ?? "null"}");
        }
#endif

        private static bool IsUnsupportedDataSubscriptionRefresh(CommandErrorResponse response)
        {
            var message = response != null ? response.Message ?? string.Empty : string.Empty;
            if (string.IsNullOrWhiteSpace(message))
                return false;

            return message.IndexOf("DataSubscriptionRefreshRequest", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   message.IndexOf("not supported by this module", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
