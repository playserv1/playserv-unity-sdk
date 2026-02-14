# PlayServ Server Handlers

This folder contains optional in-process handlers for server runtime scenarios.

When handlers are configured, PlayServ API can execute logic locally instead of sending websocket commands.

## Available handlers

- `ICommandHandler` / `LocalCommandHandler` for `PlayServ.Send(...)`.
- `IEventHandler` / `LocalEventHandler` for `PlayServ.Publish(...)` and `PlayServ.Subscribe(...)`.

## Setup

```csharp
using Playserv.Server;
using Playserv.Wrapper;

var commandHandler = new LocalCommandHandler()
    .RegisterFallback((command, module) =>
    {
        // Local command processing.
    });

var eventHandler = new LocalEventHandler();

PlayServ.SetCommandHandler(commandHandler);
PlayServ.SetEventHandler(eventHandler);
```

With local handlers configured, you can call `PlayServ.Send`, `PlayServ.Publish`, and `PlayServ.Subscribe`
without establishing websocket connection.

## Fallback behavior

- If local handler handles call (`Try... == true`), transport is skipped.
- If local handler does not handle call:
  - SDK falls back to transport when connected.
  - SDK throws descriptive error when transport is not connected.
