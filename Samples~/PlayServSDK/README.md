# PlayServ SDK Samples

This folder contains ready-to-run examples of the PlayServ client SDK for Unity.
Each sample can be launched as a standalone scene and then adapted to your game code.

## What is included

For typed function/RPC response contracts, opt into strict JSON types per call:

```csharp
var code = await PlayServCode.CallAsync<RewardResponse>("reward", new { round = 1 },
    new PlayServFunctionCallOptions { StrictResponseTypes = true });
var rpc = await PlayServRpc.InvokeAsync<MatchRequest, MatchResponse>(
    "MatchService", "Find", request,
    new PlayServRpcInvokeOptions { StrictResponseTypes = true });
```

`RewardResponse`, `MatchRequest` and `MatchResponse` are game-owned DTOs. Import
`Playserv.Wrapper`, `Playserv.Code` and `Playserv.RPC`. Handle `IsSuccess` before
using `Value`. Strict mismatches return `Deserialization` (Code) or
`DeserializationFailed` (RPC), without field values in error diagnostics. Use a
JSON string, not plain text, for strict string results. The default remains
compatible/coercing. Unsupported converters/codecs fail before a request is sent.

- `Identity/PlayServIdentityLifecycleSample.cs` - provider discovery, safe
  link/conflict/merge flow, unlink, automatic mobile fingerprint configuration,
  and custom fingerprint fallback construction.
- `1_DataSubscriptionScene.unity` - data subscription example (`SelectEntity`, internal polling registry).
- `DataSubscription/PlayServRecordsSample.cs` - typed V2 records CRUD/query,
  verified natural-key load-or-create, cursor-based LoadAll, bounded LoadMany,
  typed realtime collection subscriptions, and backend-wins synchronization
  for individual record handles.
  `LoadSavedViewAsync` demonstrates paged saved-View reads; `RenameViewItemAsync`
  reloads the partial View handle before editing so hidden fields are not lost.
- `Commerce/PlayServCommerceSample.cs` - paged live storefront discovery and
  typed catalog item loading over the runtime HTTP API.
- `Status/PlayServStatusSample.cs` - credential-free current health, daily
  availability history, and validated federation discovery.
- `Matchmaking/PlayServMatchmakingSample.cs` - typed lobby-state placement,
  SDK-managed Join polling, and player-authenticated game-server launch.
- `2_EventsScene.unity` - event pub/sub example (`Subscribe`, `Publish`).
- `3_RPC.unity` - RPC call example (`PlayServRpc.Invoke`) with `NotificationEvent` handling.
- `4_Spawn.unity` - network spawning example (`PlayServSpawn.Spawn`).

## How to run the samples

1. Import this sample from the PlayServ SDK package details in Package Manager.
2. Open the scene for the API you want to test.
3. Press Play and use `Connect SDK`. The scene resolves the active project
   configuration through the SDK.

For an authenticated connection popup and a single command-driven diagnostics
scene, install `com.playserv.debug-terminal`, enable `Debug Terminal` in the
PlayServ module settings, and import its **Debug Terminal** sample.

Identity credentials must come from the platform provider at runtime. The
identity sample deliberately accepts them as method arguments and acquires a
fresh proof before merge; it does not serialize tokens into a scene or asset.

---

## 1_DataSubscriptionScene

### Purpose
Shows live `Configuration(id: "default")` state from the tanks schema, local mutations, and full refresh in polling or transport subscription mode.

### What it demonstrates
- `PlayServData.SelectEntity<TEntity, TDto>(...)`.
- `PlayServData.Records<T>()` handles with server IDs, snapshots, ETags, queries, and `LoadOrCreateAsync`.
- Query generation for the `Configuration` entity from the current schema/model shape.
- Handling `Changed`, `Error`, and `Terminated`.
- Local updates: `Update(...)`, `UpdateAsync(...)`.
- Forced state refresh: `RefreshAsync()`.
- Internal subscription engine:
  - in-memory subscription registry,
  - `DataGetRequest` polling every 3 seconds,
  - update callback only when payload diff is detected.

### How to use
1. Connect to SDK.
2. Click `Bind Polling` or `Bind Transport` to subscribe to `Configuration(id=default)`.
3. Try:
   - `Tank Speed +0.25`,
   - `Max HP +1`,
   - `Heal +1`,
   - `Boost Combat Async`,
   - `Reset Defaults`.
4. Watch `Current value` and `Logs`.
5. Click `Unbind` to stop the subscription.

Note: both backends are available in this SDK path:
- transport bind uses `module_dataflow.DataSubscriptionRequest` / `DataSubscriptionUpdate`
- transport mutation buttons use `module_dataflow.DataMutationRequest` / `DataMutationResponse`
- transport refresh uses `module_dataflow.DataSubscriptionRefreshRequest` / `DataSubscriptionUpdate`
- polling mode keeps the compatibility path via `DataGetRequest`

### What you can build with it
- Live gameplay config tuning.
- Tank movement/combat/pickup balancing UI.
- Optimistic local updates with server reconciliation.

---

## 2_EventsScene

### Purpose
Shows event channels for global, group, and user-targeted scenarios.

### What it demonstrates
- Subscription: `PlayServEvents.Subscribe<SampleChatEvent>(...)`.
- Group membership: `PlayServEvents.SubscribeGroupAsync(groupName)` and `PlayServEvents.UnsubscribeGroupAsync(groupName)`.
- Typed capability rejection for the legacy `PlayServEvents.Publish*` methods;
  the current backend supports subscriptions but not client event publishing.
- Safe unsubscription via `Dispose()`.

### How to use
1. Connect to SDK.
2. Click `Subscribe`.
3. Click `Join Group` if you want to receive group-scoped events for the configured group.
4. The legacy publish buttons demonstrate the immediate typed capability error;
   use RPC, Code or Records for client-to-server messages.
   - `Publish Global`,
   - `Publish Group`,
   - `Publish User`.
5. Check incoming messages in `Logs`.
6. Click `Leave Group` and `Unsubscribe` when done.

Important: group event delivery requires both the typed event subscription and
`PlayServEvents.SubscribeGroupAsync(groupName)`.

### What you can build with it
- Receive backend-originated notifications and system events.
- Observe group-scoped lobby, match or clan events.

---

## 3_RPC

### Purpose
Shows backend RPC invocation and receiving the outcome through events.

### What it demonstrates
- RPC call via expression: `PlayServRpc.Invoke<TService>(x => x.Method(...))`.
- RPC call via fast positional path: `PlayServRpc.InvokeArgs(serviceName, methodName, args...)`.
- RPC call via fast named path: `PlayServRpc.InvokeNamed(serviceName, methodName, payload)`.
- `NotificationEvent` subscription for transport-level responses/notifications.
- Transport error logging via `PlayServ.OnTransportError`.

### How to use
1. Connect to SDK.
2. Click `Subscribe` (for `NotificationEvent`).
3. Click one of:
   - `Invoke Expr`
   - `Invoke Args`
   - `Invoke Named`
4. Check `Logs` for:
   - outbound RPC (`-> ...`),
   - inbound event (`<- ...`).
5. Click `Unsubscribe` to stop receiving events.

### What you can build with it
- Gameplay RPC operations (rewards, matchmaking, match actions).
- Push-style notifications after RPC execution.
- Unified transport flow for RPC + events.
- Fast-path RPC calls without expression parsing in hot paths.

---

## 4_Spawn

### Purpose
Shows networked prefab spawning at runtime.

### What it demonstrates
- `PlayServSpawn.Spawn(assetName, position, rotation)`.
- Reading `NetworkObject` and `NetworkId`.
- Basic prefab validation checks.

### Requirements
- Prefab must be in `Resources`.
- Prefab must include `NetworkObject`.
- Add `NetworkTransform` for movement sync (optional).

### How to use
1. Connect to SDK.
2. Verify `assetName` (prefab name from `Resources`).
3. Click `Spawn Random`.
4. Check status and `Logs`.

### What you can build with it
- Online spawning for players, bots, and world objects.
- Dynamic level entities with network identity.
- A base for transform replication flows.

---

## Useful notes

- All sample overlays are full-screen and include in-window usage hints.
- If controls do not work, check `SDK state` and `Logs` first.
- You can copy these scripts into production code as a baseline.
