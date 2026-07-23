# PlayServ SDK Samples

This folder contains ready-to-run examples of the PlayServ client SDK for Unity.
Each sample can be launched as a standalone scene and then adapted to your game code.

## What is included

- `Samples.unity` (`0_Samples`) - scene hub and base connection controls.
- `Common/PlayServBootstrapSample.cs` - shared bootstrap for SDK config and connection.
- `1_DataSubscriptionScene.unity` - data subscription example (`SelectEntity`, internal polling registry).
- `2_EventsScene.unity` - event pub/sub example (`Subscribe`, `Publish`).
- `3_RPC.unity` - RPC call example (`PlayServRpc.Invoke`) with `NotificationEvent` handling.
- `4_Spawn.unity` - network spawning example (`PlayServSpawn.Spawn`).

## How to run all samples

1. Import this sample from the PlayServ SDK package details in Package Manager.
2. Open `Samples.unity` from the imported sample folder.
3. On the `PlayServBootstrap` object, set:
   `gameId`, `userId`, `gameVersion`, and a runtime credential:
   `clientToken` (`pk_*`) or `authorization` (`Bearer sk_*` / player JWT).
4. Enable `autoConnect` if you want automatic connection on Play Mode start.
5. Press Play.
6. In `PlayServ Samples Hub (0_Samples)`:
   - click `Connect SDK` (if auto-connect is disabled),
   - open any sample scene.

Important: `PlayServBootstrapSample` uses `DontDestroyOnLoad`, so the same connection is reused across sample scenes.

---

## 0_Samples (Hub + Bootstrap)

### Purpose
Single entry point for all demo scenes: configuration, connect/disconnect, and scene navigation.

### What it demonstrates
- Base initialization through `PlayServ.Config(PlayServSettings)`.
- Reusing one SDK connection across multiple scenes.
- Basic SDK state monitoring.

### How to use
1. Configure `PlayServBootstrap` in the Inspector.
2. Connect (`Connect SDK`).
3. Open a scene from the `Scenes` list.
4. Use `Back to 0_Samples` inside each sample scene when needed.

### What you can build with it
- A single persistent bootstrap for your entire game.
- No repeated `Connect()` calls during scene transitions.

---

## 1_DataSubscriptionScene

### Purpose
Shows live `Configuration(id: "default")` state from the tanks schema, local mutations, and full refresh in polling or transport subscription mode.

### What it demonstrates
- `PlayServData.SelectEntity<TEntity, TDto>(...)`.
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
- Publishing:
  - `PlayServEvents.Publish(...)`,
  - `PlayServEvents.PublishForGroup(...)`,
  - `PlayServEvents.PublishForUser(...)`.
- Safe unsubscription via `Dispose()`.

### How to use
1. Connect to SDK.
2. Click `Subscribe`.
3. Click `Join Group` if you want to receive group-scoped events for the configured group.
4. Send events with:
   - `Publish Global`,
   - `Publish Group`,
   - `Publish User`.
5. Check incoming messages in `Logs`.
6. Click `Leave Group` and `Unsubscribe` when done.

Important:
- Group event delivery requires both:
  - `PlayServEvents.Subscribe<SampleChatEvent>(...)`
  - `PlayServEvents.SubscribeGroupAsync(groupName)`
- Publishing to a group does not automatically join that group.

### What you can build with it
- Chat, notifications, and system alerts.
- Group broadcasts (lobby, match, clan).
- User-targeted messaging.

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
- Every scene includes a `Back to 0_Samples` button.
- If controls do not work, check `SDK state` and `Logs` first.
- You can copy these scripts into production code as a baseline.
