using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Playserv.RPC;

namespace Playserv.Wrapper
{
    internal sealed class RpcPendingRequestRegistry
    {
        private const char RouteSeparator = '\u001f';

        private readonly object _sync = new object();
        private readonly Dictionary<string, PendingRpcRequest> _requestsById =
            new Dictionary<string, PendingRpcRequest>(StringComparer.Ordinal);
        private readonly Dictionary<string, LinkedList<PendingRpcRequest>> _requestsByRoute =
            new Dictionary<string, LinkedList<PendingRpcRequest>>(StringComparer.Ordinal);

        public PendingRpcRequest Register(string requestId, string serviceName, string methodName)
        {
            var routeKey = BuildRouteKey(serviceName, methodName);
            var pending = new PendingRpcRequest(requestId, routeKey);

            lock (_sync)
            {
                if (_requestsById.ContainsKey(requestId))
                {
                    throw new InvalidOperationException(
                        $"An RPC request with id '{requestId}' is already pending.");
                }

                if (!_requestsByRoute.TryGetValue(routeKey, out var route))
                {
                    route = new LinkedList<PendingRpcRequest>();
                    _requestsByRoute.Add(routeKey, route);
                }

                pending.RouteNode = route.AddLast(pending);
                _requestsById.Add(requestId, pending);
            }

            return pending;
        }

        public bool TryTake(InvokeRpcResponse response, out PendingRpcRequest pending)
        {
            pending = null;
            if (response == null)
                return false;

            var responseRequestId = ResolveResponseRequestId(response);
            lock (_sync)
            {
                if (!string.IsNullOrWhiteSpace(responseRequestId))
                {
                    if (!_requestsById.TryGetValue(responseRequestId, out pending))
                        return false;

                    RemoveLocked(pending);
                    return true;
                }

                var request = response.Request;
                if (request == null ||
                    string.IsNullOrWhiteSpace(request.ServiceName) ||
                    string.IsNullOrWhiteSpace(request.MethodName))
                {
                    return false;
                }

                var routeKey = BuildRouteKey(request.ServiceName, request.MethodName);
                if (!_requestsByRoute.TryGetValue(routeKey, out var route) || route.First == null)
                    return false;

                pending = route.First.Value;
                RemoveLocked(pending);
                return true;
            }
        }

        public bool TryRemove(PendingRpcRequest pending)
        {
            if (pending == null)
                return false;

            lock (_sync)
            {
                if (!_requestsById.TryGetValue(pending.RequestId, out var current) ||
                    !ReferenceEquals(current, pending))
                {
                    return false;
                }

                RemoveLocked(pending);
                return true;
            }
        }

        private void RemoveLocked(PendingRpcRequest pending)
        {
            _requestsById.Remove(pending.RequestId);

            var node = pending.RouteNode;
            var route = node?.List;
            if (route == null)
                return;

            route.Remove(node);
            pending.RouteNode = null;

            if (route.Count == 0)
                _requestsByRoute.Remove(pending.RouteKey);
        }

        private static string ResolveResponseRequestId(InvokeRpcResponse response)
        {
            if (!string.IsNullOrWhiteSpace(response.RequestId))
                return response.RequestId;

            return response.Request?.RequestId;
        }

        private static string BuildRouteKey(string serviceName, string methodName)
        {
            return serviceName + RouteSeparator + methodName;
        }
    }

    internal sealed class PendingRpcRequest
    {
        public PendingRpcRequest(string requestId, string routeKey)
        {
            RequestId = requestId;
            RouteKey = routeKey;
            Completion = new TaskCompletionSource<InvokeRpcResponse>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public string RequestId { get; }

        public string RouteKey { get; }

        public TaskCompletionSource<InvokeRpcResponse> Completion { get; }

        public LinkedListNode<PendingRpcRequest> RouteNode { get; set; }
    }
}
