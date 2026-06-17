using Playserv.Proxy.Common;

namespace Playserv.RPC
{
    internal sealed class RpcCommandRouteProvider : ICommandRouteProvider
    {
        public void RegisterRoutes(PlayServCommandRouteRegistry routes)
        {
            routes.Register("InvokeRpcResponse", command => OnInvokeRpcResponseReceived(routes, command));
            routes.Register("rpc.InvokeRpcResponse", command => OnInvokeRpcResponseReceived(routes, command));
            routes.Register("rpc.InvokeRpc.InvokeRpcResponse", command => OnInvokeRpcResponseReceived(routes, command));
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
