using Playserv.Proxy.Common;
using Playserv.Serialization;

namespace Playserv.RPC
{
    internal sealed class RpcCommandRouteProvider : ICommandRouteProvider
    {
        public void RegisterRoutes(PlayServCommandRouteRegistry routes)
        {
            routes.Register("RpcErrorResponse", command => OnCommandErrorReceived(routes, command));
            routes.Register("rpc.RpcErrorResponse", command => OnCommandErrorReceived(routes, command));
            routes.Register("InvokeRpcResponse", command => OnInvokeRpcResponseReceived(routes, command));
            routes.Register("rpc.InvokeRpcResponse", command => OnInvokeRpcResponseReceived(routes, command));
            routes.Register("rpc.InvokeRpc.InvokeRpcResponse", command => OnInvokeRpcResponseReceived(routes, command));
        }

        private static void OnCommandErrorReceived(PlayServCommandRouteRegistry routes, object command)
        {
            var response = command as CommandErrorResponse;
            if (response != null)
            {
                routes.Logger.LogError(
                    $"Server command error received. Error: {response.Error}, Message: {response.Message}, Timestamp: {response.Timestamp}");
                return;
            }

            routes.Logger.LogWarning($"Received RPC error command with unexpected payload type: {command?.GetType().Name ?? "null"}");
        }

        private static void OnInvokeRpcResponseReceived(PlayServCommandRouteRegistry routes, object command)
        {
            var response = command as InvokeRpcResponse;
            if (response != null)
            {
                var requestInfo = response.Request == null
                    ? "n/a"
                    : $"{response.Request.ServiceName}.{response.Request.MethodName}";
                var resultInfo = string.IsNullOrWhiteSpace(response.Result) ? "<empty>" : response.Result;
                routes.Logger.Log(
                    $"InvokeRpcResponse received. status={response.Status}, message={response.Message}, request={requestInfo}, result={resultInfo}");
                routes.NotifyModuleCommand("InvokeRpcResponse", response);
                return;
            }

            routes.Logger.LogWarning($"[PlayServ][RPC] Received InvokeRpcResponse with unexpected payload type: {command?.GetType().Name ?? "null"}");
        }
    }
}
