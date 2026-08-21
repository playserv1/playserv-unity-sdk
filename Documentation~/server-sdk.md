# PlayServ Server SDK

This document describes both the local server/shared runtime and the optional
Unity Dedicated Server HTTP companion.

## Unity Dedicated Server companion

Install `com.playserv.game-server` when a Unity Dedicated Server executable
must call PlayServ's existing server-only runtime endpoints. The companion is
separate from the player SDK credential path: it accepts only `sk_*`, sends only
`Authorization: Bearer sk_*`, and refuses operations in ordinary client builds
before it reads a credential or starts HTTP. Editor execution is allowed for
tests.

Configure `Playserv.GameServer.PlayServGameServer` from environment variables
`PLAYSERV_API_URL` and `PLAYSERV_SERVER_KEY`, or supply
`PlayServGameServerOptions` with a rotating `IPlayServServerKeyProvider`.
Credentials are resolved for every request and are never persisted or logged.

The package supports server matchmaking with explicit player IDs, server
launch, room list/upsert/close, reservation consumption, safe player lookup,
typed Records, Cloud Functions and multiple independent room heartbeat loops.
Records use `acl.server` with ETags and the existing query/singleton APIs;
functions use the existing `/fn/{slug}` gateway. Server Analytics uses a
bounded retry-safe queue with per-event player IDs, while Catalog and
Storefront reads reuse the player runtime's typed query/page models. Calling
`PlayServGameServer.Realtime.ConnectAsync` additionally enables collection and
record subscriptions through an independent rotating-server-key WebSocket
session. `ValidatePlayerTokenAsync` performs credential-free RS256/JWKS checks
for early admission but does not replace authoritative revocation or reservation
checks. None of these surfaces adds a public client token or player JWT.
Graceful hosts should await `PlayServGameServer.ShutdownAsync` before process
exit and inspect its additive `AnalyticsError`. See the companion's
`README.md` and Dedicated Server sample for a complete setup.

## Scope

The repository has two related delivery targets:

- **Unity SDK package**: installed through Unity Package Manager or imported
  under `Assets`.
- **Server/shared runtime assembly**: built by a separate .NET host project that
  includes the compatible source subset from this package.

Publishing the Unity package does not build the server/shared runtime assembly.

## Source Layout

Server/shared runtime source is taken from this package's `Runtime` folder.

Important server-related folders:

- `Runtime/Modules/Server/Implementation` - in-process server module, local command/event handlers, server RPC invoker.
- `Runtime/Modules/RPC/Core` - RPC attribute and shared RPC payload utilities.
- `Runtime/Modules/Events/Implementation` - event payloads and event routing abstractions.
- `Runtime/Modules/DataSubscription/Implementation` - shared data subscription abstractions and request/response contracts.
- `Runtime/Serialization` - JSON abstraction and Newtonsoft implementation.
- `Runtime/Proxy/Common` - shared transport envelopes, handshake contracts, keepalive/reconnect types.

Unity-only files are excluded by the server/shared runtime `.csproj`, including Unity config assets, Unity HTTP module, the project-generated Unity module selection, and Spawn module code.

## Build Server/Shared Runtime

The .NET host repository must provide `PlayServ.Shared.Runtime.csproj`. Set
`PlayServSdkRuntimeRoot` to the absolute path of this package's `Runtime`
directory.

Restore:

```bash
dotnet restore PlayServ.Shared.Runtime.csproj \
  -p:PlayServBuildRoot="$PWD/" \
  -p:PlayServSdkRuntimeRoot="/path/to/playserv-unity-sdk/Runtime/"
```

Build release:

```bash
dotnet build PlayServ.Shared.Runtime.csproj -c Release \
  -p:PlayServBuildRoot="$PWD/" \
  -p:PlayServSdkRuntimeRoot="/path/to/playserv-unity-sdk/Runtime/"
```

The default output is controlled by the host project. A typical output path is:

```text
exports/PlayServ.Shared.Runtime/bin/Release/PlayServ.Shared.Runtime.dll
```

## Optional Module Defines

The server/shared runtime `.csproj` supports module stripping through `DefineConstants`.

Example:

```bash
dotnet build PlayServ.Shared.Runtime.csproj -c Release \
  -p:PlayServBuildRoot="$PWD/" \
  -p:PlayServSdkRuntimeRoot="/path/to/playserv-unity-sdk/Runtime/" \
  -p:DefineConstants="PLAYSERV_MODULE_DISABLED_DATA;PLAYSERV_MODULE_DISABLED_TRANSPORT_WEBRTC"
```

Supported defines:

- `PLAYSERV_MODULE_DISABLED_EVENTS`
- `PLAYSERV_MODULE_DISABLED_DATA`
- `PLAYSERV_MODULE_DISABLED_RPC_CORE`
- `PLAYSERV_MODULE_DISABLED_CLIENT_RPC`
- `PLAYSERV_MODULE_DISABLED_SERVER`
- `PLAYSERV_MODULE_DISABLED_PULSE`
- `PLAYSERV_MODULE_DISABLED_TRANSPORT_WEBSOCKET`
- `PLAYSERV_MODULE_DISABLED_TRANSPORT_UDP`
- `PLAYSERV_MODULE_DISABLED_TRANSPORT_RUDP`
- `PLAYSERV_MODULE_DISABLED_TRANSPORT_WEBRTC`
- `PLAYSERV_MODULE_DISABLED_CLIENT_EXECUTION`

Notes:

- `PLAYSERV_MODULE_DISABLED_RPC_CORE` also removes server RPC related code.
- `PLAYSERV_MODULE_DISABLED_SERVER` removes `Runtime/Modules/Server`.
- Spawn is currently excluded from `PlayServ.Shared.Runtime.csproj` by default.

## Server Runtime Usage

The server module provides local/in-process execution hooks. Typical setup:

```csharp
using Playserv.RPC;
using Playserv.Server;
using Playserv.Wrapper;

[Rpc]
public sealed class NotificationService
{
    public void BroadcastToAll(string message)
    {
        // Server-side game logic here.
    }
}

public static class ServerSdkBootstrap
{
    public static void Configure()
    {
        var rpcInvoker = new ServerRpcInvoker()
            .RegisterService(new NotificationService());

        PlayServServerRpc.SetRpcInvoker(rpcInvoker);

        PlayServServerRpc.SetCommandHandler(
            new LocalCommandHandler()
                .RegisterFallback((command, moduleName) =>
                {
                    // Route outgoing commands to backend module bus/proxy.
                }));

        PlayServServerRpc.SetEventHandler(new LocalEventHandler());
    }
}
```

Main server-facing types:

- `Playserv.Wrapper.PlayServServerRpc`
- `Playserv.Server.ServerRpcInvoker`
- `Playserv.Server.LocalCommandHandler`
- `Playserv.Server.LocalEventHandler`
- `Playserv.Server.ICommandHandler`
- `Playserv.Server.IEventHandler`
- `Playserv.RPC.RpcAttribute`

## Validation Checklist

Before publishing or handing the SDK to backend:

1. Build server/shared runtime:

```bash
dotnet build PlayServ.Shared.Runtime.csproj -c Release \
  -p:PlayServBuildRoot="$PWD/" \
  -p:PlayServSdkRuntimeRoot="/path/to/playserv-unity-sdk/Runtime/"
```

2. Check the output DLL exists:

```bash
ls exports/PlayServ.Shared.Runtime/bin/Release/PlayServ.Shared.Runtime.dll
```

3. Check Unity package metadata if shipping Unity SDK:

```bash
cat /path/to/playserv-unity-sdk/package.json
```

4. Verify no Unity-only APIs leaked into the server/shared runtime build. The `.csproj` already excludes known Unity-only files, but new runtime files should be reviewed before release.

## CI Notes

Recommended CI steps for server/shared runtime:

```bash
dotnet restore PlayServ.Shared.Runtime.csproj \
  -p:PlayServBuildRoot="$PWD/" \
  -p:PlayServSdkRuntimeRoot="/path/to/playserv-unity-sdk/Runtime/"
dotnet build PlayServ.Shared.Runtime.csproj -c Release --no-restore \
  -p:PlayServBuildRoot="$PWD/" \
  -p:PlayServSdkRuntimeRoot="/path/to/playserv-unity-sdk/Runtime/"
```

Recommended CI artifacts:

- `exports/PlayServ.Shared.Runtime/bin/Release/PlayServ.Shared.Runtime.dll`
- Any required third-party assemblies resolved by the backend host/runtime.

Do not use Unity batchmode package export as a substitute for this `.NET` build.
