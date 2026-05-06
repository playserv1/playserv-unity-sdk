using System;
using Playserv.Proxy.Logging;

namespace Playserv.Proxy.Common
{
    internal sealed class ProxyCommandRouteProvider : ICommandRouteProvider
    {
        public void RegisterRoutes(PlayServCommandRouteRegistry routes)
        {
            if (routes == null)
                throw new ArgumentNullException(nameof(routes));

            routes.Register("error", command => OnCommandErrorReceived(routes.Logger, command));
            routes.Register("CommandErrorResponse", command => OnCommandErrorReceived(routes.Logger, command));
            routes.Register("Disconnect", command => routes.TransportSession.HandleDisconnect());
            routes.Register("ForcedDisconnect", command => routes.TransportSession.HandleForcedDisconnect(command as ForcedDisconnectResponse));
            routes.Register("module_proxy.ForcedDisconnect", command => routes.TransportSession.HandleForcedDisconnect(command as ForcedDisconnectResponse));
            routes.Register("ClientSettingsResponse", command => OnClientSettingsResponseReceived(routes, command));
            routes.Register("ParseErrorResponse", command => OnParseErrorReceived(routes.Logger, command));
            routes.Register("ValidationErrorResponse", command => OnValidationErrorReceived(routes.Logger, command));
        }

        private static void OnClientSettingsResponseReceived(PlayServCommandRouteRegistry routes, object command)
        {
            var response = command as ClientSettingsResponse;
            if (response != null)
            {
                routes.TransportSession.HandleClientSettingsResponse(response);
                return;
            }

            routes.Logger.LogWarning($"Received ClientSettingsResponse with unexpected payload type: {command?.GetType().Name ?? "null"}");
        }

        private static void OnParseErrorReceived(ILogger logger, object command)
        {
            var response = command as ParseErrorResponse;
            if (response != null)
            {
                logger.LogError($"Parse error received from server. Error: {response.Error}, Received JSON: {response.ReceivedJson}");
                return;
            }

            logger.LogWarning($"Received ParseErrorResponse with unexpected payload type: {command?.GetType().Name ?? "null"}");
        }

        private static void OnValidationErrorReceived(ILogger logger, object command)
        {
            var response = command as ValidationErrorResponse;
            if (response != null)
            {
                logger.LogError($"Validation error received from server. Error: {response.Error}, Received JSON: {response.ReceivedJson}");
                return;
            }

            logger.LogWarning($"Received ValidationErrorResponse with unexpected payload type: {command?.GetType().Name ?? "null"}");
        }

        private static void OnCommandErrorReceived(ILogger logger, object command)
        {
            var response = command as CommandErrorResponse;
            if (response != null)
            {
                if (IsUnsupportedDataSubscriptionRefresh(response))
                {
                    logger.LogWarning(
                        $"Server command warning received. Error: {response.Error}, Message: {response.Message}, Timestamp: {response.Timestamp}");
                    return;
                }

                logger.LogError(
                    $"Server command error received. Error: {response.Error}, Message: {response.Message}, Timestamp: {response.Timestamp}");
                return;
            }

            logger.LogWarning($"Received 'error' command with unexpected payload type: {command?.GetType().Name ?? "null"}");
        }

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
