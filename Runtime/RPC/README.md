# PlayServ RPC

This folder contains runtime RPC helpers for calling server services via:

- `PlayServ.Invoke(serviceName, methodName, payload)`
- `PlayServ.Invoke(serviceName, methodName, payloadBase64)`
- `PlayServ.Invoke<TService>(x => x.SomeMethod("arg"))`
- `PlayServ.Invoke<TService>(x => x.SomeMethod(default), payload)`

Internally, PlayServ sends `RpcInvokeRequest` through module:

- `rpc.InvokeRpc`

## Payload format

RPC payload is sent as:

1. JSON
2. UTF8 bytes
3. Base64 string

This matches the RPC example from `Assets/Tests/RPC`.

## Example

```csharp
using Playserv.Wrapper;

PlayServ.Invoke(
    serviceName: "NotificationService",
    methodName: "BroadcastToAll",
    payload: new { message = "Hello" });
```

Equivalent base64 variant:

```csharp
PlayServ.Invoke(
    "NotificationService",
    "BroadcastToAll",
    "eyJtZXNzYWdlIjoiSGVsbG8ifQ==");
```

Expression-based variant:

```csharp
PlayServ.Invoke<NotificationService>(
    x => x.BroadcastToAll(default),
    new { message = "Hello" });
```

Function-call variant (auto payload from function arguments):

```csharp
PlayServ.Invoke<NotificationService>(
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
