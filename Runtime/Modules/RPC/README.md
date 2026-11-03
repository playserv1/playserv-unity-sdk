# PlayServ RPC

This folder contains runtime RPC helpers for calling server services via:

- `PlayServRpc.Invoke(serviceName, methodName, payload)`
- `PlayServRpc.InvokeAsync<TRequest, TResponse>(serviceName, methodName, request, cancellationToken)`
- `PlayServRpc.Invoke(serviceName, methodName, payloadBase64)`
- `PlayServRpc.Invoke<TService>(x => x.SomeMethod("arg"))`
- `PlayServRpc.Invoke<TService>(x => x.SomeMethod(default), payload)`

Internally, PlayServ sends `RpcInvokeRequest` through module:

- `rpc.InvokeRpc`

RPC is split into `Runtime/Modules/RPC/Core` and `Runtime/Modules/RPC/Client`.
Server-side in-process RPC lives in the optional `Server` module under `Runtime/Modules/Server`.
If a server RPC invoker is configured with `PlayServServerRpc.SetRpcInvoker(...)`, invocation is executed in-process and websocket transport is skipped.
If invoker is configured but service is not registered, SDK falls back to transport (or throws if transport is not connected).
When the Server module is disabled, `Playserv.Server.ServerRpcInvoker` and `PlayServServerRpc.SetRpcInvoker(...)` are intentionally unavailable while client RPC stays enabled. `IRpcInvoker` remains the RPC contract in `Playserv.RPC`.

## Payload format

RPC payload is sent as:

1. JSON
2. UTF8 bytes
3. Base64 string

This matches the RPC example from `Assets/Tests/RPC`.

## Example

```csharp
using Playserv.Wrapper;

PlayServRpc.Invoke(
    serviceName: "NotificationService",
    methodName: "BroadcastToAll",
    payload: new { message = "Hello" });
```

## Awaitable typed RPC

Use `InvokeAsync` for request/response operations that need a typed result:

```csharp
using System.Threading;
using System.Threading.Tasks;
using Playserv.RPC;
using Playserv.Wrapper;

public async Task<FindMatchResponse> FindMatchAsync(
    FindMatchRequest request,
    CancellationToken cancellationToken)
{
    PlayServRpcResult<FindMatchResponse> result =
        await PlayServRpc.InvokeAsync<FindMatchRequest, FindMatchResponse>(
            "RoomService",
            "FindMatch",
            request,
            cancellationToken);

    if (!result.IsSuccess)
    {
        throw new InvalidOperationException(
            $"RPC {result.RequestId} failed: {result.Error}");
    }

    return result.Value;
}
```

Operational failures are returned as `PlayServRpcError` with one of these
categories:

- `SerializationFailed`
- `TransportFailed`
- `Timeout`
- `Canceled`
- `ServerError`
- `InvalidResponse`
- `DeserializationFailed`

The default timeout is 30 seconds. Use explicit options to change it or supply
an idempotency/correlation identifier:

```csharp
var options = new PlayServRpcInvokeOptions
{
    RequestId = matchRequestId,
    Timeout = TimeSpan.FromSeconds(10),
    CoalesceKey = playerId
};

var result = await PlayServRpc.InvokeAsync<FindMatchRequest, FindMatchResponse>(
    "RoomService",
    "FindMatch",
    request,
    options,
    cancellationToken);
```

The SDK sends `RequestId` in `InvokeRpc` and accepts it either at the
`InvokeRpcResponse` root or in `InvokeRpcResponse.Request`. Gateways that do not
echo request ids remain compatible through oldest-first matching scoped to the
same service and method. Do not run concurrent calls to the same service and
method against such an older gateway when out-of-order replies are possible.

`InvokeAsync` always uses client transport. In-process server invocation still
uses the synchronous `PlayServRpc.Invoke` surface.

Equivalent base64 variant:

```csharp
PlayServRpc.Invoke(
    "NotificationService",
    "BroadcastToAll",
    "eyJtZXNzYWdlIjoiSGVsbG8ifQ==");
```

Expression-based variant:

```csharp
PlayServRpc.Invoke<NotificationService>(
    x => x.BroadcastToAll(default),
    new { message = "Hello" });
```

Function-call variant (auto payload from function arguments):

```csharp
PlayServRpc.Invoke<NotificationService>(
    x => x.BroadcastToAll("Hello"));
```

Service type must be decorated with `[Rpc]`:

```csharp
using Playserv.RPC;

[Rpc]
public class NotificationService
{
    public void BroadcastToAll(string message) { }
}
```

For this variant payload is built automatically as:

```json
{ "message": "Hello" }
```

`TService` type name is used as `serviceName`, and method call name is used as `methodName`.

## Server-side (no websocket) usage

```csharp
using Playserv.RPC;
using Playserv.Wrapper;

var invoker = new Playserv.Server.ServerRpcInvoker()
    .RegisterService(new NotificationService(context));

PlayServServerRpc.SetRpcInvoker(invoker);

// Executes local method directly, does not send command over websocket.
PlayServRpc.Invoke<NotificationService>(x => x.BroadcastToAll("Hello from server"));
```
