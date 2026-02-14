# PlayServ Server Mode Usage

This folder contains local in-process handlers for server runtime scenarios.

When local handlers are configured, `PlayServ` API can be used without websocket transport:

- `PlayServ.Send(...)` via `ICommandHandler`
- `PlayServ.Publish(...)` / `PlayServ.Subscribe(...)` via `IEventHandler`
- `PlayServ.Invoke(...)` via `IRpcInvoker` (`LocalRpcInvoker` in `Runtime/RPC`)

## Quick start

```csharp
using Playserv.RPC;
using Playserv.Server;
using Playserv.Wrapper;

[Rpc]
public sealed class NotificationService
{
    public void Broadcast(string message)
    {
        // Server logic
    }
}

var commandHandler = new LocalCommandHandler()
    .RegisterFallback((command, module) =>
    {
        // Handle PlayServ.Send(...) locally.
    });

var eventHandler = new LocalEventHandler();

var rpcInvoker = new LocalRpcInvoker()
    .RegisterService(new NotificationService());

PlayServ.SetCommandHandler(commandHandler);
PlayServ.SetEventHandler(eventHandler);
PlayServ.SetRpcInvoker(rpcInvoker);
```

After this setup you can call `PlayServ.Send`, `PlayServ.Publish`, `PlayServ.Subscribe`, and `PlayServ.Invoke`
without calling `PlayServ.Connect()`.

## API usage on server

### 1) Commands (`Send`)

```csharp
PlayServ.Send(new CreateMatchCommand { Region = "eu" }, "server.match.create");
```

`LocalCommandHandler` receives module name and payload in registered handlers.

### 2) Events (`Publish` / `Subscribe`)

```csharp
var sub = PlayServ.Subscribe<MatchReadyEvent>(evt =>
{
    // Local event callback
});

PlayServ.Publish(new MatchReadyEvent { MatchId = "m-123" });
```

`LocalEventHandler` dispatches events by payload type.

### 3) RPC (`Invoke`)

```csharp
PlayServ.Invoke<NotificationService>(x => x.Broadcast("hello"));
```

`LocalRpcInvoker` resolves service/method and executes it in-process.

## Disable local mode

```csharp
PlayServ.SetCommandHandler(null);
PlayServ.SetEventHandler(null);
PlayServ.SetRpcInvoker(null);
```

## Fallback behavior

- Local handler returns `true` -> call is handled locally, transport is skipped.
- Local handler returns `false` and websocket transport is connected -> SDK falls back to transport.
- Local handler returns `false` and transport is not connected -> SDK throws descriptive exception.

## Notes

- RPC service class must have `[Rpc]` attribute.
- `LocalRpcInvoker` does not support ambiguous method overloads with the same method name.
- `LocalEventHandler` group/user publish methods currently route by event type (group/user IDs are validated, but not used for filtering).
