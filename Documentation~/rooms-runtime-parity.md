# Rooms runtime integration

## Launch metadata

```csharp
var name = PlayServServerLaunch.GetRoomName(); // null for a studio-run process
var connect = PlayServServerLaunch.ResolveConnect(listenPort, explicitConnect);
// Pass these explicitly when constructing the game's room snapshot.
```

Only neutral `PLAYSERV_*` launch variables are read. Mapping supports port-keyed objects (plain port, `/udp`, `/tcp`) and entries with `internal_port`, `external_port`, `protocol`. No matching mapping uses the known listen port. Invalid values are rejected; no guessed localhost/LAN endpoint or vendor-specific discovery is used. The game still owns room preparation and transport startup.

For a process assigned one room, `PlayServGameServer.StartHostedRoomAsync(transport, options, ct)` composes this lifecycle. It reads the platform launch, authenticates the uplink, waits for the game-owned `IPlayServHostedRoomTransport` listener, then registers the room. `PlayServServerLaunch.ReadHosted()` exposes a validated immutable metadata snapshot without starting networking. The current provisioning path uses `PLAYSERV_PUBLIC_HOST` and `/run/playserv/deployment-token`; legacy `PLAYSERV_PUBLIC_IP`, port mappings and the deployment-token environment variable remain supported. The deployment identity is used in both hello and room registration.

The returned `PlayServHostedRoom` owns cleanup through `StopAsync`; global shutdown and room removal also stop its listener. An uncertain registration or unconfirmed cleanup prevents a new hosted start in that process. See the [companion hosted-room guide](../CompanionPackages~/com.playserv.game-server/README.md#a-platform-launched-room) for callback ownership, deadlines and credential precedence. The explicit manual room API retains its existing configuration and shared-uplink behavior.

## Configuration and lifecycle

```csharp
var config = await PlayServGameServer.GetRoomConfigurationAsync("arena", ct);
PlayServGameServer.Uplink.StateChanged += state => UpdateConnectionUi(state);
PlayServGameServer.Uplink.ConfigurationChanged += current => UpdateCapacityUi(current?.Capacity);
```

Reading configuration does not connect the uplink or overwrite its authoritative facts. Events are ordered and delivered outside mutation locks on the configured Unity context; replaced uplink instances cannot deliver into the new configuration. Unchanged states and repeated configuration versions do not emit duplicate events.

## Platform-requested room creation refusals

The optional `RoomFactory` uses the authoritative uplink configuration and the same
registration path as an explicit room start. `PlayServRoomCreateDecision.Refuse(...)`
accepts `RoomNameConflict`, `InstanceDraining`, `RoomQuotaExceeded`, `RoomCreateFailed`
and `ContentRefused`.
An occupied name takes precedence over quota. Active **and pending** rooms count
towards `max_rooms`: reaching it returns `room_quota_exceeded`, not `instance_draining`.
Closing a room frees capacity for later requests without reconnecting.

A synchronous exception or faulted factory task returns `room_create_failed`; its
`detail` contains only the short exception type name (at most 128 characters), never
the message, stack, inner exception or payload. The reserved name is released;
`RoomCreationCompleted` and the local `room_factory_failed` diagnostic still notify
the game to clean up preparation. An independently canceled factory also follows
this path if the SDK request token is still active.

Neither quota, factory failure nor content refusal removes the instance from future routing. Only
`instance_draining` expresses that it is leaving selection. SDK timeout, disconnect
and shutdown do not produce late results. Invalid factory results retain their local
validation behavior. Success still precedes registration, and registration/network
failure after `ok: true` never sends a second `room_create_result` or retries the upsert.

These mappings follow uplink §1.3, including Story 15.20's optional requested attributes.
Backend senders for player hosting (PSV-2626) and platform creation (PSV-2590) are
implemented in `dev`; fixture tests do not verify the deployed router.

### Apply, alter or refuse requested content

The existing `RoomFactory(request, ct)` receives `request.Attributes` as a copied
JSON object: omitted/`null` stays `null`, `{}` stays empty, and each getter returns an
independent copy. Only flat scalar values within 2048 serialized UTF-8 bytes are
accepted. Invalid attributes produce the local `room_create_invalid_request`
diagnostic without factory invocation, wire refusal or upsert.

Treat this data as an untrusted player's wish. A factory may pass approved values
to `PlayServGameRoomSnapshot(..., attributes: approved)`, replace them with its own
values, ignore them, or return
`PlayServRoomCreateDecision.Refuse(PlayServRoomCreateRefusal.ContentRefused, "Unsupported map")`.
Do not echo input or secrets in `detail`. Content refusal sends one `content_refused`
result, releases the name, keeps the instance eligible and reports completion for
game-owned cleanup. It does not register a room.

The returned snapshot alone determines registration/heartbeat attributes; there
is no automatic merge. The delegate signature and existing factories remain
compatible, and a frame without attributes follows the previous path. See the
companion README and requested-room sample for a map/mode allowlist example.

## Game connection lifecycle

For event-driven integration, attach `PlayServRoomNetworkAdapter(room, network)`
on Unity's main thread. The optional adapter owns the tracker and subscribes to
`IPlayServRoomNetworkLifecycle.Authenticating` / `Disconnected`; the game implements
`Accept(verifiedPlayerId)` and `Close(reason)` on each unique physical connection.
The source must gate gameplay until Accept and close the exact connection on Close.
Callbacks are dispatched onto Unity's context, late refusals are admission-specific,
and explicit disposal or room shutdown detaches the source. Scene changes preserve
bindings while the network connection survives; actual reconnects use fresh tickets.
See the [adapter guide](../CompanionPackages~/com.playserv.game-server/README.md#optional-game-network-adapter)
and importable **TCP Network Lifecycle** sample for the full callback contract,
error handling and existing operator-handler ownership.

The lower-level manual integration remains:

```csharp
var tracker = room.CreateConnectionTracker(TimeSpan.FromSeconds(30));
tracker.ConnectionRemoved += removal => DisconnectExactConnection(removal.ConnectionId);
var admission = tracker.TryAdmit(connectionGenerationId, reservationToken);
// Refuse the transport connection when !admission.Ok. On a transient drop:
tracker.ReportDisconnected(connectionGenerationId);
// On explicit quit/kick instead:
tracker.RemovePlayer(admission.PlayerId);
```

Pushed tickets are the only supported reservation admission path (PSV-3181);
the old HTTP consume API and its opt-out have been removed. The tracker is optional:
without it, call `room.Admission.TryAdmit` and handle `AdmissionRejected` yourself.
Use fresh connection IDs and fresh tickets on reconnect. A dropped player holds its seat during grace; duplicate drop notifications do not extend it. A kick bypasses grace. No networking library is selected, and no socket is closed by the SDK. Keep the rejection subscription alive until shutdown completes. Do not mix tracker ownership and manual roster mutation for the same players.

## Bounded client join

```csharp
// Call on Unity's main thread; connectGameTransport is owned by your networking integration.
var ticket = await PlayServMatchmaking.JoinRoomAndConnectAsync(
    new PlayServJoinRoomRequest { FunctionSlug = "arena", RoomName = selectedRoom },
    connector: connectGameTransport, ct: ct);
```

Without a connector this returns a usable reservation only. Defaults are 45 seconds overall, 30 seconds waiting for an address, one-second polling and three `room_unreachable` retries. No find/launch fallback, retry of arbitrary network failures, or retry after a connector failure. Retry-After applies within the total budget. The connector must honor cancellation; cancellation cannot roll back a game connection. Tokens are passed separately, not appended to an engine-specific URL.

These APIs use the existing platform protocol. Fixture tests are not evidence of a deployed end-to-end integration. Never log reservation tokens or admission parameters.

## Named join parameters

```csharp
var match = await PlayServMatchmaking.JoinRoomAsync(new PlayServJoinRoomRequest
{
    FunctionSlug = "arena", RoomName = "room-one", Params = new { team = "blue" }
}, ct);
// On a trusted dedicated server, reserve a named room for a player:
var serverMatch = await PlayServGameServer.JoinRoomForPlayerAsync(
    "arena", "room-one", playerId, new { team = "blue" }, ct);
```

Client identity always comes from its authenticated session. Only the trusted server call includes `player_id`. Parameters must be a JSON object; they are copied before awaiting credentials. The admission hook decides whether their contents are acceptable. Both calls perform one request only. The original string-based client overload still sends no body.
## Runtime schema advisory

```csharp
#if DEVELOPMENT_BUILD || UNITY_EDITOR
var checks = await PlayServData.CheckSchemaAsync(new[] { typeof(PlayerProfile) }, ct);
foreach (var check in checks) Debug.Log($"{check.EntityName}: {check.Availability}");
// Dedicated server: PlayServGameServer.CheckSchemaAsync uses server credentials.
#endif
```

This reads `/data/tables` only. `NotVisible` is not proof of physical absence: catalogue ACL applies. `Unavailable` includes failed reads and ambiguous type names. Matching follows Records CLR-name resolution (exact first, then unique case-insensitive); explicitly ID-bound Records with different names are outside this advisory. The manual API keeps accepting an explicit type list without scanning assemblies. For attributed entities, `Compatibility` compares the observed singleton/table kind; missing kind metadata stays `Unknown`. It does not verify field types or change schemas.

To opt into automatic checks, enable **Enable Automatic Schema Advisory** on the
PlayServ config asset (also available in the Settings window), or set
`PlayServSettings.EnableAutomaticSchemaAdvisory = true` before `PlayServ.Config`.
The default is `false`. Disabled sessions perform neither discovery nor a catalog
request. No Schema Tool process or platform server key is needed at runtime.

After the first successful anonymous, restored, external or browser sign-in, the
optional Data module queues discovery and one catalog read without awaiting them
in the sign-in operation. An unmanaged token becomes eligible after a successful
connection handshake. Discovery includes closed `[PlayServSchema]` classes with
`Kind = Auto` or `Entity`, and structs explicitly marked `Entity`; Part, Enum,
Editor-only and test assemblies are excluded. Partial assembly loads retain the usable types.
Builds generate linker roots under `Library/PlayServ/SchemaAdvisory` for active
entity contracts, including player-only declarations and referenced DLL contracts,
so runtime discovery can work with managed stripping and IL2CPP. These roots are
generated even if the config asset disables the check, because code can opt in later.

One check is attempted per SDK runtime session/configuration scope. Token refresh,
reconnect and repeated auth notifications do not retry it. Explicit reconfiguration
or a new Play session allows a new attempt. Identity replacement, logout and shutdown
cancel obsolete work; late results from another identity/configuration are discarded.
Catalog reads are bounded by `TimeoutSeconds` (clamped to 1–120 seconds). Check again
manually after correcting a transient failure.

Matching visible entities produce no warning. A `singleton_mismatch` names the
entity and expected/observed kind. `not_in_visible_catalogue` asks you to check the
environment and read permissions before modifying schema. Failed or ambiguous
catalog reads and missing kind metadata report that compatibility is unknown.
Diagnostics use the Unity context and never include HTTP bodies or credentials;
advisory failure does not change authentication success or block gameplay.
## Typed server RPC (opt-in)

```csharp
var amount = new PlayServServerRpcParameter<int>("amount");
var registry = new PlayServServerRpcRegistry();
registry.Register<int>("score.preview", new[] { amount }, (caller, args, ct) =>
{
    ct.ThrowIfCancellationRequested();
    return Task.FromResult(args.Get(amount));
});
// Configure on Unity's main thread:
PlayServGameServer.Configure(new PlayServGameServerOptions { RpcRegistry = registry });
```

Accepted payload JSON is `[5]` or `{"amount":5}`, encoded as base64 UTF-8 in the existing RPC envelope. The SDK does not define a second networking handshake. Numeric strings, fractional integers, out-of-range values, unknown arguments and absent required arguments fail safely. Default arguments are explicit descriptor values. Keep DTO constructors and members with `[Preserve]` or a game-owned `link.xml` under IL2CPP.

Only registry configuration advertises `rpc`. The bounded serial handler budget includes queue time. Cancellation is local, not rollback; no `rpc_cancel` is invented. A game handler that ignores its token keeps its slot until completion, even after a timeout/error response. The platform's authenticated caller context does not widen server REST/Records credentials. Use application authorization in each handler. These tests are fixtures, not live integration evidence.
