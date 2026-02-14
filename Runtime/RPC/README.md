# PlayServ RPC

This folder contains runtime RPC helpers for calling server services via:

- `PlayServ.Invoke(serviceName, methodName, payload)`
- `PlayServ.InvokeBase64(serviceName, methodName, payloadBase64)`

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
PlayServ.InvokeBase64(
    "NotificationService",
    "BroadcastToAll",
    "eyJtZXNzYWdlIjoiSGVsbG8ifQ==");
```
