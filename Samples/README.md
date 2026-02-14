# PlayServ SDK Samples

This folder contains runtime API samples split by feature.

## Folder structure

- `Samples/Common` - connection/bootstrap sample.
- `Samples/Events` - event publish/subscribe sample.
- `Samples/DataSubscription` - shared entity subscription sample.
- `Samples/Spawn` - network spawn sample.
- `Samples/RPC` - RPC invoke sample (transport and local in-process modes).
- `Samples/Server` - full server-mode sample (command + events + rpc in-process).

## Quick setup for a scene

1. Create a new Unity scene.
2. Add an empty GameObject `PlayServBootstrap`.
3. Attach `PlayServBootstrapSample` (`Samples/Common/PlayServBootstrapSample.cs`).
4. Fill credentials and endpoint in inspector.
5. Add one feature sample component to another GameObject:
   - `PlayServEventsSample`
   - `PlayServDataSubscriptionSample`
   - `PlayServSpawnSample`
   - `PlayServRpcSample`
   - `PlayServServerModeSample`
6. Press Play.

## Scene recipes

### 1) Connection scene

- Add only `PlayServBootstrapSample`.
- Use overlay buttons (`Configure`, `Connect`, `Disconnect`).

### 2) Events scene

- Add `PlayServBootstrapSample`.
- Add `PlayServEventsSample`.
- Connect first, then use publish buttons (global/group/user).

### 3) Data subscription scene

- Add `PlayServBootstrapSample`.
- Add `PlayServDataSubscriptionSample`.
- Click `Bind` in component context menu or use overlay buttons.
- Use `Rename`, `Add Level`, `Set Level`, `Refresh`.

### 4) Spawn scene

- Add `PlayServBootstrapSample`.
- Add `PlayServSpawnSample`.
- Ensure prefab exists in `Resources` under selected `Asset Name`.
- Prefab must contain `NetworkObject`.
- Optional: add `NetworkTransform` on prefab for transform sync.

### 5) RPC scene

- Add `PlayServBootstrapSample`.
- Add `PlayServRpcSample`.
- Use `Enable Local` to execute `PlayServ.Invoke(...)` in-process (no websocket send).
- Use `Disable Local` to fallback to normal transport RPC (`rpc.InvokeRpc`).

### 6) Server mode scene

- Add `PlayServServerModeSample`.
- Click `Enable Handlers`.
- Use buttons:
  - `Send Command` to test `PlayServ.Send(...)` via local command handler.
  - `Publish Event` to test `PlayServ.Publish(...)` and local subscribe callback.
  - `Invoke RPC` to test `PlayServ.Invoke(...)` via local rpc invoker.
- No websocket connection is required while handlers are enabled.

## Notes

- `PlayServBootstrapSample` can auto-connect on play.
- All samples are intentionally simple and use IMGUI overlays to avoid extra UI dependencies.
- You can copy scripts into your game code and adapt logic/UI as needed.
