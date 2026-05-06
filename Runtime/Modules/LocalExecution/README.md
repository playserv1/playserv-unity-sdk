# PlayServ Local Execution

This module family provides the local execution path for `PlayServ` API.

Use this when you want to run gameplay/backend logic on server **without websocket transport** and **without adding server logic into this SDK project**.

## Module layout

- `Core` contains the internal local-execution contract shared by client/server slices.
- `Client` contains the removable client-side no-op bridge used by transport-backed SDK builds.
- `Server` is the removable server-side slice with default in-process implementations: `LocalCommandHandler` and `LocalEventHandler`.

When `PLAYSERV_MODULE_DISABLED_CLIENT_EXECUTION` is defined, the client execution slice is unavailable. Editor module settings also disable client-facing modules that depend on it (`Events`, client `RPC`, `Pulse`, and their dependents).

When both client and server local-execution slices are disabled, `PLAYSERV_MODULE_DISABLED_LOCAL_EXECUTION_CORE` removes the shared internal local-execution contract.

When `PLAYSERV_MODULE_DISABLED_LOCAL_EXECUTION_SERVER` is defined, the server-side slice is unavailable:
`PlayServ.SetCommandHandler(...)`, `PlayServ.SetEventHandler(...)`, `PlayServServerRpc.SetRpcInvoker(...)`,
`ICommandHandler`, `IEventHandler`, `LocalCommandHandler`, and `LocalEventHandler` are intentionally removed from the public SDK surface.

## Recommended architecture

Keep server logic in a separate project/repository (for example `playserv-game-server`):

1. Reference `Playserv.Runtime.Core.dll` (or package) in your server project.
2. Implement your domain logic there (commands, events, RPC services).
3. Register local handlers via:
   - `PlayServ.SetCommandHandler(...)`
   - `PlayServ.SetEventHandler(...)`
   - `PlayServServerRpc.SetRpcInvoker(...)`
4. Call `PlayServ.Send/Publish/Subscribe/Invoke` from server code as usual.

No changes are required in SDK runtime code for this.

## How SDK decides server vs client behavior

`PlayServ` does not auto-detect environment by OS/process type.

Behavior is selected by configuration:

- If local handler/invoker is set and handles call -> handled locally (server mode path).
- If local handler/invoker is missing or returns `false` -> SDK uses transport path (`Connect` + websocket), if available.
- If local handler/invoker returns `false` and transport is not connected -> SDK throws clear exception.

## Server bootstrap example

```csharp
using Playserv.Events;
using Playserv.RPC;
using Playserv.Server;
using Playserv.Wrapper;

[Rpc]
public sealed class NotificationService
{
    public void Broadcast(string message)
    {
        // Your server logic here
    }
}

[Event(EventType.All)]
public sealed class MatchReadyEvent
{
    public string MatchId { get; set; } = string.Empty;
}

public sealed class CreateMatchCommand
{
    public string Region { get; set; } = "eu";
}

public static class PlayServServerBootstrap
{
    private static LocalCommandHandler? _commandHandler;
    private static LocalEventHandler? _eventHandler;
    private static ServerRpcInvoker? _rpcInvoker;

    public static void Start()
    {
        _commandHandler = new LocalCommandHandler()
            .RegisterModule("server.match.create", command =>
            {
                var create = (CreateMatchCommand)command;
                // Run domain logic for match creation.
            })
            .RegisterFallback((command, moduleName) =>
            {
                // Optional: generic routing/logging.
            });

        _eventHandler = new LocalEventHandler();

        _rpcInvoker = new ServerRpcInvoker()
            .RegisterService(new NotificationService());

        PlayServ.SetCommandHandler(_commandHandler);
        PlayServ.SetEventHandler(_eventHandler);
        PlayServServerRpc.SetRpcInvoker(_rpcInvoker);
    }

    public static void Stop()
    {
        PlayServ.SetCommandHandler(null);
        PlayServ.SetEventHandler(null);
        PlayServServerRpc.SetRpcInvoker(null);
        PlayServ.Disconnect();
    }
}
```

## API mapping in server mode

- `PlayServ.Send(command, module)` -> `ICommandHandler.TryHandle(command, module)`
- `PlayServ.Publish(evt)` -> `IEventHandler.TryPublish(evt)`
- `PlayServ.PublishForGroup(group, evt)` -> `IEventHandler.TryPublishForGroup(group, evt)`
- `PlayServ.PublishForUser(userId, evt)` -> `IEventHandler.TryPublishForUser(userId, evt)`
- `PlayServ.Subscribe<T>(...)` -> `IEventHandler.TrySubscribe<T>(...)`
- `PlayServ.Invoke<TService>(...)` -> `IRpcInvoker.TryInvoke(service, method, payload)`

## Practical usage pattern

- For pure server runtime: register handlers and do not call `PlayServ.Connect()`.
- For mixed mode (local first, transport fallback): keep handlers and also connect transport.
- For shutdown: clear handlers (`null`) and disconnect once.

## Constraints and notes

- RPC service type must have `[Rpc]` attribute.
- `ServerRpcInvoker` does not support ambiguous overloads with same method name.
- `LocalEventHandler` routes by payload type; group/user values are validated but not used as filters.
