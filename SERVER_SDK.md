# PlayServ Server SDK

This document describes the server-side SDK/runtime parts that are shared with the Unity SDK and how to build them outside Unity.

## Scope

The repository has two related delivery targets:

- **Unity SDK package**: exported from `Assets/playserv-unity-sdk` as `.unitypackage` or OpenUPM package.
- **Server/shared runtime assembly**: built with `dotnet` from `PlayServ.Shared.Runtime.csproj` and consumed by backend/server code.

The Unity package export documented in `Assets/Editor/CI/README.md` does not build the server/shared runtime assembly. It only packages Unity assets from `Assets/playserv-unity-sdk`.

## Source Layout

Server/shared runtime source is taken from:

- `Assets/playserv-unity-sdk/Runtime`

Important server-related folders:

- `Runtime/Modules/Server/Implementation` - in-process server module, local command/event handlers, server RPC invoker.
- `Runtime/Modules/RPC/Core` - RPC attribute and shared RPC payload utilities.
- `Runtime/Modules/Events/Implementation` - event payloads and event routing abstractions.
- `Runtime/Modules/DataSubscription/Implementation` - shared data subscription abstractions and request/response contracts.
- `Runtime/Serialization` - JSON abstraction and Newtonsoft implementation.
- `Runtime/Proxy/Common` - shared transport envelopes, handshake contracts, keepalive/reconnect types.

Unity-only files are excluded by the server/shared runtime `.csproj`, including Unity config assets, Unity HTTP module, the project-generated Unity module selection, and Spawn module code.

## Build Server/Shared Runtime

From the Unity SDK project root:

```bash
cd /Users/reaper/PlayServ/playserv-sdk-client
```

Restore:

```bash
dotnet restore PlayServ.Shared.Runtime.csproj \
  -p:PlayServBuildRoot="$PWD/" \
  -p:PlayServSdkRuntimeRoot="$PWD/Assets/playserv-unity-sdk/Runtime/"
```

Build release:

```bash
dotnet build PlayServ.Shared.Runtime.csproj -c Release \
  -p:PlayServBuildRoot="$PWD/" \
  -p:PlayServSdkRuntimeRoot="$PWD/Assets/playserv-unity-sdk/Runtime/"
```

Default output:

```text
/Users/reaper/PlayServ/playserv-sdk-client/exports/PlayServ.Shared.Runtime/bin/Release/PlayServ.Shared.Runtime.dll
```

Debug output uses:

```text
/Users/reaper/PlayServ/playserv-sdk-client/exports/PlayServ.Shared.Runtime/bin/Debug/
```

## Optional Module Defines

The server/shared runtime `.csproj` supports module stripping through `DefineConstants`.

Example:

```bash
dotnet build PlayServ.Shared.Runtime.csproj -c Release \
  -p:PlayServBuildRoot="$PWD/" \
  -p:PlayServSdkRuntimeRoot="$PWD/Assets/playserv-unity-sdk/Runtime/" \
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

## Relationship To Legacy ServerSDK Folder

There may be legacy local copies such as:

- `/Users/reaper/PlayServ/ServerSDK`
- `/Users/reaper/PlayServ/PlayServ.Shared.Runtime`

Do not treat these as the source of truth unless a task explicitly says so. The unified SDK source should be kept in:

```text
/Users/reaper/PlayServ/playserv-sdk-client/Assets/playserv-unity-sdk/Runtime
```

If a legacy server SDK copy is still needed, sync it from the unified runtime source and then build it separately. Avoid editing the legacy copy and the Unity SDK runtime independently.

## Sync To Game Projects

For Unity game projects such as Tanks, sync only the SDK package folder:

```bash
rsync -a \
  --exclude='.git' \
  --exclude='.gitignore' \
  /Users/reaper/PlayServ/playserv-sdk-client/Assets/playserv-unity-sdk/ \
  /Users/reaper/PlayServ/playserv-tanks-jxproxy/Assets/playserv-unity-sdk/
```

This does not touch root project editor scripts under:

```text
/Users/reaper/PlayServ/playserv-tanks-jxproxy/Assets/Editor
```

Use `--delete` only when intentionally removing target files that no longer exist in the SDK source.

## Validation Checklist

Before publishing or handing the SDK to backend:

1. Build server/shared runtime:

```bash
dotnet build PlayServ.Shared.Runtime.csproj -c Release \
  -p:PlayServBuildRoot="$PWD/" \
  -p:PlayServSdkRuntimeRoot="$PWD/Assets/playserv-unity-sdk/Runtime/"
```

2. Check the output DLL exists:

```bash
ls exports/PlayServ.Shared.Runtime/bin/Release/PlayServ.Shared.Runtime.dll
```

3. Check Unity package metadata if shipping Unity SDK:

```bash
cat Assets/playserv-unity-sdk/package.json
```

4. Verify no Unity-only APIs leaked into the server/shared runtime build. The `.csproj` already excludes known Unity-only files, but new runtime files should be reviewed before release.

## CI Notes

Recommended CI steps for server/shared runtime:

```bash
cd /Users/reaper/PlayServ/playserv-sdk-client
dotnet restore PlayServ.Shared.Runtime.csproj \
  -p:PlayServBuildRoot="$PWD/" \
  -p:PlayServSdkRuntimeRoot="$PWD/Assets/playserv-unity-sdk/Runtime/"
dotnet build PlayServ.Shared.Runtime.csproj -c Release --no-restore \
  -p:PlayServBuildRoot="$PWD/" \
  -p:PlayServSdkRuntimeRoot="$PWD/Assets/playserv-unity-sdk/Runtime/"
```

Recommended CI artifacts:

- `exports/PlayServ.Shared.Runtime/bin/Release/PlayServ.Shared.Runtime.dll`
- Any required third-party assemblies resolved by the backend host/runtime.

Do not use Unity batchmode package export as a substitute for this `.NET` build.
