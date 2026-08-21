# PlayServ Debug Terminal

`com.playserv.debug-terminal` is an optional development companion package for
`com.playserv.sdk`. It provides an authenticated connection popup and an
in-game command terminal for checking the PlayServ connection, player identity,
analytics, cloud functions, commerce, matchmaking, data subscriptions, events,
RPC, and spawning from one scene.

The module is disabled by default and is intended for development and smoke
testing. It does not replace production game UI or backend integration tests.

## Requirements

- PlayServ Unity SDK `0.4.0` or newer.
- Unity `2021.3` or newer, within the compatibility range of the core SDK.
- A PlayServ project with a public `pk_*` client token, game ID, game version,
  and reachable backend endpoint.
- `com.playserv.analytics` installed at the same version as the core SDK.
- `com.playserv.game-server` installed at the same version as the core SDK.
- The following modules enabled: Analytics, Client Execution, Data
  Subscription, Events, RPC, and Spawn.

The client terminal supports Editor, Standalone, Android, iOS, and WebGL. The
optional `server` command extension is compiled only in the Editor or a
`UNITY_SERVER` build. Some commands also require matching schemas, services,
or assets in the target project.

## Install and Enable

### PlayServ SDK UI

1. Install or update `com.playserv.sdk`.
2. Open `Tools > PlayServ > Settings`.
3. Open the SDK module settings and find `Debug Terminal` under companion
   packages.
4. Select `Install` if the package is not present.
5. Enable `Debug Terminal`. The SDK also validates its required core modules.
6. Open Unity Package Manager, select `PlayServ Debug Terminal`, and import the
   `Debug Terminal` sample.

Installation and enablement are separate states:

- **Installed** means the UPM package files are present in the project.
- **Enabled** means its assemblies are included in compilation and its API and
  sample scripts are available.

Disabling the module adds
`PLAYSERV_MODULE_DISABLED_DEBUG_TERMINAL`, which excludes the package assemblies
through their assembly definition constraints without deleting package files.

### Git Dependency

Use the same tag or commit for the core SDK and this companion package:

```json
{
  "dependencies": {
    "com.playserv.sdk": "git@github.com:playserv1/playserv-unity-sdk.git#<tag-or-commit>",
    "com.playserv.analytics": "git@github.com:playserv1/playserv-unity-sdk.git?path=/CompanionPackages~/com.playserv.analytics#<tag-or-commit>",
    "com.playserv.game-server": "git@github.com:playserv1/playserv-unity-sdk.git?path=/CompanionPackages~/com.playserv.game-server#<tag-or-commit>",
    "com.playserv.debug-terminal": "git@github.com:playserv1/playserv-unity-sdk.git?path=/CompanionPackages~/com.playserv.debug-terminal#<tag-or-commit>"
  }
}
```

The repository is private, so the current user or CI runner must have Git SSH
access. Do not mix different tags or commits between the core and companion
packages.

## Import and Run the Sample

After import, Unity copies the sample into a versioned project folder similar
to:

```text
Assets/Samples/PlayServ Debug Terminal/<version>/Debug Terminal/
```

Open `DebugTerminal.unity` and enter Play Mode. The scene consists of two
reusable prefab instances:

- **PlayServ Connection** owns SDK configuration, the startup popup, anonymous
  player authentication, the transport handshake, and connection lifetime.
- **PlayServ Debug Terminal** owns the terminal overlay, command history, logs,
  and module smoke-test commands.

The scene also contains the camera and lighting needed by the spawn example.
Both prefabs can be placed in a separate project debug scene. The connection
object persists across scene loads and rejects duplicate bootstrap instances.

Imported samples are project-owned copies. Updating or removing the UPM package
does not automatically update or delete files already imported under `Assets`.

## Startup Connection Popup

In a normal, non-batch Play Mode session, the connection prefab displays a
modal popup before any connection attempt. Defaults are resolved from the
active PlayServ environment and `PlayServConfig`.

| Field | Purpose |
| --- | --- |
| Client Token | Public PlayServ project token. It must start with `pk_`. |
| Game ID | Game identifier used for the player session and handshake. |
| Game Version | Exact version sent for this session. Latest-version lookup is disabled for the popup connection. |
| Backend Address | Absolute `ws`, `wss`, `http`, `https`, `udp`, `rudp`, or `webrtc` endpoint. |
| Allow Multiple Connections | Allows the same player session to open multiple connections when supported by the backend. |

Pressing `Connect` performs this sequence:

1. Clone the resolved project settings in memory.
2. Apply the values entered in the popup.
3. Create or restore an anonymous player session over HTTP.
4. Resolve the player JWT through the SDK runtime session.
5. Open the configured transport and complete the authenticated handshake.
6. Close the popup only after the session is authenticated and online.

If validation, authentication, or the handshake fails, the popup remains open
and shows an inline error. Repeated clicks are blocked while a connection is in
progress.

Popup values apply only to the current run. The module does not modify
`PlayServConfig.asset`. Keepalive ping and pong settings are not exposed in the
popup; they remain inherited from the resolved project configuration.

When the popup is disabled, or Unity runs in batch mode, the bootstrap uses the
resolved project settings directly. Its `Auto Connect` field controls whether a
connection is started automatically in that mode.

## Terminal Controls

- Press the backquote key (`` ` ``) to show or hide the terminal.
- Press `Return` to execute the current command.
- Press `Tab` to autocomplete a command.
- Press the Up and Down arrow keys to navigate command history.
- Run `help` to print the command list in the terminal.

The terminal stays hidden while the startup connection popup is visible. It
keeps at most 300 log entries by default; this and other defaults can be changed
on the imported terminal prefab.

## Command Model

Commands are grouped by capability. `help` prints every command and usage form,
and `Tab` completes both top-level commands and subcommands such as
`record subscribe`. Quoted arguments keep spaces, for example
`record create "Player One" 3`.

The original flat commands remain available for compatibility. `bind`,
`unbind`, and `refresh` are legacy data-subscription aliases; `rpc named`,
`rpc args`, `rpc expr`, and `spawn [assetName]` retain their existing behavior.

## Commands

### SDK and Diagnostics

| Command | Description |
| --- | --- |
| `sdk info` | Print SDK version, connection state, game ID/version, endpoint, session, latest-version lookup, and keepalive configuration. |
| `sdk modules` | Print registered runtime module IDs and whether each module is selected. |
| `sdk latest [gameId]` | Resolve the latest backend version for the supplied game or the configured game. |
| `state` | Print the current connection state and short authentication summary. |
| `keepalive status` | Print ping/pong counts and the local time of the last ping and pong. |
| `keepalive reset` | Reset terminal-owned keepalive counters without changing SDK configuration. |
| `errors [count]` | Print the newest structured SDK errors from a bounded 50-entry buffer. The default count is `10`. |
| `errors clear` | Clear the local error buffer. |

Error output includes the stable `Code`, original `SourceCode`, HTTP or
transport code when present, and `Retryable`. `RawDetails` is deliberately not
printed.

### Connection and Identity

| Command | Description |
| --- | --- |
| `connect` | Connect using the settings currently configured in the SDK. |
| `disconnect` | Disconnect the active PlayServ transport and session. |
| `state` | Print SDK connection state and a short authentication summary. |
| `auth` | Print the current player authentication summary and providers. |
| `auth refresh` | Open a secure modal and rotate the live player access token through `RefreshPlayerAuthAsync`. |
| `providers` | Request the authentication providers configured for the project. |
| `logout` | Log out the managed player session. |

### Identity Lifecycle

| Command | Description |
| --- | --- |
| `identity session` | Print the managed session, linked providers, and the last typed identity conflict. |
| `identity login <provider> [preserve\|recover]` | Open the credential modal and log in with an external provider. `preserve` is the default. |
| `identity link <provider>` | Open the credential modal and link a provider to the current player. |
| `identity unlink <provider>` | Unlink a provider through the core identity API. |
| `identity merge <current\|conflicting>` | Resolve the last typed conflict, keeping either the current or conflicting player. |

Login, link, merge, and access-token refresh never accept a credential in the
command line. The terminal opens a password-style modal with the provider
credential, optional provider mode, and optional nonce. The credential exists
only in memory, is omitted from command history and logs, and is cleared after
submit or cancel. A merge is allowed only after a login or link operation has
returned a typed conflict.

### Legacy Data Subscription

| Command | Description |
| --- | --- |
| `bind <playerId> [polling\|transport]` | Bind a shared `Player` entity using polling by default or transport updates. |
| `unbind` | Dispose the current player binding. |
| `rename <new name>` | Update the bound player's `Nickname`. A generated name is used when omitted. |
| `addlevel [amount]` | Increment the bound player's `Level`; the default amount is `1`. |
| `setlevel <value>` | Set the bound player's `Level` to an exact integer. |
| `refresh` | Fetch the latest state of the bound player entity. |

The sample expects a compatible backend `Player` entity with `Nickname` and
`Level` fields. The default player ID is `player-001`. Data commands report an
error when the schema or entity is not available.

### Typed Records V2

The terminal uses this fixed typed set:

```csharp
PlayServData.Records<DebugTerminalPlayerDto>("Player")
```

`DebugTerminalPlayerDto` contains `Nickname` and `Level`.

| Command | Description |
| --- | --- |
| `record create <nickname> [level]` | Create a server-ID record. Level defaults to `1`. |
| `record load <recordId>` | Load one record by server ID and make it active. |
| `record loadorcreate <nickname> [level]` | Load by `Nickname` natural key or create a record. |
| `record query [options]` | Query records and retain the page, cursor, and first result as active. |
| `record next` | Load the next page with the last query and cursor. |
| `record subscribe [options]` | Open one realtime typed collection. Unsupported realtime query features return a capability error. |
| `record loadall [options]` | Follow cursors into a retained batch, bounded by `--max-records`. |
| `record loadmany <ids>` | Load comma-separated record IDs with bounded concurrency. |
| `record populate <id>` / `record populatemany <ids>` | Resolve one or several relation record IDs. |
| `record deletebyid <id> [--etag value]` | Delete without first loading a handle. |
| `record batch status\|set\|save\|delete` | Inspect, mutate, save, or explicitly delete the retained batch. |
| `record deleteall [options] --confirm matching\|all` | Non-atomically delete a complete query result with explicit destructive intent. |
| `record capabilities [refresh]` | Print client/server/backend ACL capabilities. `refresh` bypasses cached table metadata. |
| `record watch` | Subscribe the active `PlayServRecord<T>` handle to realtime snapshots. |
| `record unwatch` | Close the active record-level realtime lease. |
| `record save nickname <text>` | Change and save the active record or singleton nickname. |
| `record save level <number>` | Change and save the active record or singleton level. |
| `record reload` | Reload the active record or singleton, including its ETag. |
| `record delete` | Delete the active non-singleton record. |
| `record singleton` | Load the current player's `Player` singleton. |
| `record status` | Print active data, ETag, changed fields, overwrite conflict, deletion, page, and subscription state. |
| `record close` | Close collection and record-level subscriptions, then clear retained handles. |

Record query options:

| Option | Meaning |
| --- | --- |
| `--nickname <value>` | Exact `Nickname` filter. |
| `--min-level <number>` | `Level >= number`. |
| `--search <text>` | Backend full-text search. |
| `--sort nickname\|level` | Sort field. |
| `--desc` | Descending sort direction. |
| `--limit <1-200>` | Page size; default `50`. |
| `--cursor <value>` | Explicit cursor. |
| `--fields nickname,level` | Positive field projection. A projected record is partial until reloaded. |
| `--or <json-object>` | Realtime-only OR group using `nickname` and/or `minLevel`; repeat up to eight times. |
| `--include <relation.path>` | Include a direct or nested relation path; repeat as needed. Nested paths are realtime-only. |

The backend must expose the `Player` entity with compatible `Nickname` and
`Level` fields. `record loadorcreate` treats `Nickname` as a natural key; for
deterministic concurrency behavior, configure that backend field as unique.

### Subscription Lifecycle

| Command | Description |
| --- | --- |
| `subscription status` | Print legacy, collection, and record-watch state plus terminal errors. |
| `subscription refresh [legacy\|records\|record\|all]` | Request and await a fresh server snapshot without opening another subscription. |
| `subscription close [legacy\|records\|record\|all]` | Detach callbacks and await `CloseAsync`; defaults to `all`. |
| `subscription errors` | Print the bounded list of structured subscription failures. |

The terminal listens to both `Failure` and `Terminated`. Explicit close uses
`CloseAsync`; component shutdown uses best-effort disposal.

### Analytics

| Command | Description |
| --- | --- |
| `analytics status` | Print collection state, queued event count, and custom-provider state. |
| `analytics enable\|disable` | Enable or disable collection for the current runtime. |
| `analytics user <userId\|clear>` | Set or clear analytics user context. |
| `analytics property set <key> <value>` | Set one user property. |
| `analytics property remove <key>` | Remove one user property. |
| `analytics property clear` | Clear all user properties. |
| `analytics track <event> [key=value ...]` | Queue an event. JSON-looking values are parsed as typed values. |
| `analytics flush` | Flush the current batch through the configured analytics provider. |

### Cloud Functions

| Command | Description |
| --- | --- |
| `code call <slug> [options]` | POST through the typed function helper. |
| `code invoke <method> <slug> [options]` | Invoke GET, POST, PUT, PATCH, or DELETE. |
| `code bytes <method> <slug> [options]` | Invoke with optional exact bytes from `--input-file` and report response size/SHA-256. |
| `code download <method> <slug> <path> [options]` | Stream into a temporary file and atomically publish it after success. |
| `code status` | Print the active invocation or last result. |
| `code cancel` | Cancel the active invocation. |

Function options are `--body <json>`, repeatable `--query <key=value>`,
`--version <tag>`, and `--timeout <seconds>`. Response bodies are single-line,
bounded previews. Custom headers are deliberately unavailable so credentials
cannot enter command history.

Binary options additionally include `--input-file`, `--content-type`, and
`--max-response-bytes`; downloads accept `--overwrite`. `code status` reports
transfer progress. Binary payloads, response bytes, and response headers are
never printed. File operations are unavailable on WebGL.

### Public Platform and Data Catalogue

| Command | Description |
| --- | --- |
| `platform current` | Print current overall and per-PoP/system health. |
| `platform history [--pop value] [--days 1-365]` | Print the newest availability entry per system. |
| `platform federation` | Print validated peer status origins without contacting them. |
| `platform status` / `platform cancel` | Inspect or cancel the active public-status request. |
| `table list [--refresh]` | Print visible runtime table metadata and ACL capabilities. |
| `table get <idOrName> [--refresh]` | Resolve one table from the shared catalogue cache. |

### Dedicated Server

The same terminal exposes `com.playserv.game-server` in the Editor and Unity
Dedicated Server builds. Its implementation lives in a separate assembly, so
ordinary player builds keep no assembly dependency on Game Server APIs.

| Command | Description |
| --- | --- |
| `server configure [options]` | Configure backend, heartbeat and timeout. Add `--prompt` to enter an `sk_*` key through the memory-only password modal; otherwise `PLAYSERV_SERVER_KEY` is used. |
| `server status\|cancel\|shutdown` | Inspect/cancel terminal operations or explicitly shut down the global Game Server facade. |
| `server realtime connect\|disconnect\|status` | Manage the independent rotating-server-key WebSocket used by realtime Records. |
| `server room start\|upsert\|update\|heartbeat\|list\|status\|close` | Exercise managed room registration, desired snapshots, placement acknowledgements and close lifecycle. |
| `server match find\|launch` | Match an explicit player or request a server deployment. Reservation tokens are never printed. |
| `server reservation consume` | Open a secure token modal and perform authoritative room admission. |
| `server player get <playerId>` | Print the safe runtime player profile subset. |
| `server jwt validate ...` | Open a secure JWT modal and validate signature, lifetime and expected project/environment through JWKS. |
| `server acting set\|clear\|status` | Manage a memory-only acting-player context for player-owned Records writes. |
| `server code call\|invoke\|status\|cancel` | Exercise server-key Cloud Functions. Custom headers are not accepted. |
| `server analytics status\|enable\|disable\|track\|flush` | Exercise bounded server analytics with optional per-event `--player`. |
| `server catalog ...` / `server storefront ...` | Exercise server-authorized commerce reads and retained pagination. |
| `record caller client\|server\|acting` | Switch the Records/table provider and clear handles belonging to the previous caller. Server callers require active server realtime for subscriptions. |

`server configure` resolves the backend in this order: explicit `--backend`,
`PLAYSERV_API_URL`, then the current SDK setting. Server keys, player JWTs and
reservation tokens are accepted only by the secure modal. They never enter
command history, terminal logs, serialized fields or normalized error output.
Component shutdown cancels terminal operations, closes only terminal-owned room
handles and clears memory-only secrets. It does not call the process-wide
`PlayServGameServer.ShutdownAsync`; use `server shutdown` for that explicit
graceful-host action.

### Catalog and Storefront

| Command | Description |
| --- | --- |
| `catalog list [options]` / `storefront list [options]` | List a page and retain its cursor and first ID. |
| `catalog next` / `storefront next` | Follow the retained next cursor. |
| `catalog get [itemId]` / `storefront get [storefrontId]` | Load an explicit or retained active ID. |

List options are `--status`, `--search`, `--sort`, `--cursor`, and
`--limit 1-200`; storefront also supports `--audience`.

### Matchmaking

| Command | Description |
| --- | --- |
| `match find <functionSlug> [options]` | Perform one placement request. |
| `match join <functionSlug> [options]` | Run the SDK-managed placement wait loop. |
| `match launch <functionSlug> [--region value]` | Request a game-server deployment. |
| `match status` | Print active elapsed time or the last safe summary. |
| `match cancel` | Cancel the active matchmaking operation. |

Find/join accept `--matchmaker`, `--wait_ms 0-25000`, and
`--params <json-object>`; find also accepts `--search_age_ms`. Wrap JSON that
contains spaces in single quotes, for example
`--params '{"mode":"ranked duo"}'`. Reservation tokens are never printed.

### Events

| Command | Description |
| --- | --- |
| `subevent` | Subscribe to `DebugTerminalChatEvent`. |
| `unsubevent` | Dispose the chat event subscription. |
| `event subscribe-raw` | Subscribe to the same event through the raw event API. |
| `event unsubscribe-raw` | Dispose the raw event subscription. |
| `event subscribe <typed\|raw> [count]` | Add one or more local observers to the same logical event topic. |
| `event unsubscribe <typed\|raw> [one\|all]` | Release one lease or every local observer of that kind. |
| `event status` | Print typed, raw, RPC notification, and group subscription state. |
| `joingroup [group]` | Join an event group; the default is `demo-group`. |
| `leavegroup [group]` | Leave an event group. |
| `publishglobal <text>` | Publish a chat event globally. |
| `publishgroup [group] <text>` | Publish to a group. When omitted, the default group is used. |
| `publishuser [userId] <text>` | Publish to one user. The default target is `player-002`. |

Receiving group events requires joining the same group. Event behavior also
depends on the target backend's event routing and authorization configuration.

### RPC

| Command | Description |
| --- | --- |
| `subrpc` | Subscribe to RPC notifications from the sample service. |
| `unsubrpc` | Dispose the RPC notification subscription. |
| `rpc named <text>` | Invoke the sample RPC using named arguments. This is the default mode. |
| `rpc args <text>` | Invoke the sample RPC using positional arguments. |
| `rpc expr <text>` | Invoke using the expression helper where runtime expression compilation is supported. |
| `rpc await <text>` | Invoke `NotificationService.BroadcastToAll` with a typed awaitable result. |
| `rpc timeout <milliseconds>` | Set the awaitable timeout from `100` to `120000`; default `30000`. |
| `rpc cancel` | Cancel the one active awaitable invocation. |
| `rpc status` | Print request ID, elapsed time, timeout, or the last typed result/error. |

The RPC smoke test targets `NotificationService.BroadcastToAll`. Awaitable mode
uses `InvokeAsync<Dictionary<string, object>, NotificationResult>`, one active
request at a time, and reports its request ID plus unified error metadata. The
service and method must exist on the connected backend. Expression mode is
unavailable on WebGL and IL2CPP; use `named`, `args`, or `await` there.

### Spawn and Utility

| Command | Description |
| --- | --- |
| `spawn [assetName]` | Spawn a network prefab near the configured center. The default asset is `TestCube`. |
| `spawn scope` | Print the current spawn scope. |
| `spawn join <group>` | Join a spawn scope/group. |
| `spawn leave` | Leave the current spawn scope. |
| `spawn despawn [spawnId\|last]` | Despawn by network ID or the last object created by the terminal. |
| `clear` | Clear terminal output. |
| `help` | Print command usage. |

The spawn command expects the named prefab under a Unity `Resources` folder and
requires a compatible `NetworkObject`. Spawn position is randomized within the
configured radius, which defaults to `6` units around world origin.

## Prefab Configuration

Select the imported prefabs or their scene instances to customize the sample.
Edit the copies under `Assets`, not the immutable UPM package cache.

### PlayServ Connection

- `Show Startup Configuration Popup`: shows the modal connection form on each
  non-batch run.
- `Auto Connect`: fallback used when the popup is disabled or in batch mode.
- `Disconnect On Destroy`: closes the PlayServ connection when the owning
  bootstrap is destroyed.
- Resolved endpoint fields are read-only previews of the active project
  environment.

### PlayServ Debug Terminal

- `Show Overlay`: initial terminal visibility.
- `Focus Input On Enable`: focuses command input when the terminal starts.
- `Max Log Entries`: bounds retained output.
- Default player, group, target user, and spawn asset values.
- Spawn center and radius.

The sample UI is drawn by runtime IMGUI controllers and does not require a
Canvas hierarchy.

## Security and Production Builds

- Enter only a public `pk_*` client token. Never put `sk_*`, deploy tokens,
  Apple private keys, Google secrets, or backend credentials in the sample.
- Player JWT acquisition and refresh are managed by the SDK. The terminal does
  not persist or print the raw JWT. `auth refresh` accepts a runtime token only
  through the password-style secure modal.
- External provider credentials, provider modes, and nonces remain in private,
  non-serialized fields and are cleared after every submit or cancel.
- Terminal logs can include player IDs, event payloads, RPC arguments, and
  backend errors. Treat them as development data.
- Matchmaking output omits reservation tokens. Function output omits response
  headers and truncates response bodies; binary output is represented only by
  length and SHA-256. Function request headers cannot be entered through the terminal.
- The module is hidden from normal SDK export profiles and disabled by default.
  Keep the debug scene out of production Build Settings unless its inclusion is
  intentional.
- Disabling the module excludes its assemblies, but imported sample files remain
  in the project until the project removes them.

## Troubleshooting

| Problem | Check |
| --- | --- |
| Debug Terminal is not listed in PlayServ settings | Use core SDK `0.4.0` or newer, wait for Package Manager import to finish, then reopen settings or run `Repair SDK Modules`. |
| Package is installed but sample scripts do not compile | Install/enable Analytics, then enable Debug Terminal and its Client Execution, Data Subscription, Events, RPC, and Spawn dependencies. Check that the disable defines are not active unexpectedly. |
| Popup fields are empty | Select/configure the active `PlayServConfig` environment and confirm its public client token, game ID, version, and backend endpoint. |
| Client token is rejected | Use a public token beginning with `pk_`; do not use a Bearer value, `sk_*`, or deploy token. |
| Connect fails and the popup stays open | Check the backend URL, player-auth HTTP availability, game/version existence, network access, and backend handshake logs. |
| `providers` returns none | Configure identity providers for the game on the PlayServ backend. |
| `identity merge` reports no conflict | Run `identity login` or `identity link` first and retain the typed `409` conflict in the current terminal session. |
| Data commands fail | Ensure the backend exposes a compatible `Player` schema/entity and that the requested player ID exists or can be created. |
| `record loadorcreate` creates duplicates | Configure `Player.Nickname` as a unique backend field before using it as the sample natural key. |
| `record subscribe` reports a capability error | Remove query options that the realtime dataflow transport does not support; REST `record query` may support a wider grammar. |
| `record capabilities` prints `Unknown` | The current backend table catalogue does not advertise ACL metadata; unknown capabilities do not block requests. |
| Commerce `next` has no cursor | Run the corresponding `list` command first and confirm the backend returned `cursor_next`. |
| Matchmaking keeps waiting | Use `match status` and `match cancel`; verify `wait_ms` is within `0-25000`. |
| RPC returns no usable response | Ensure `NotificationService.BroadcastToAll` exists and caller authorization allows it. |
| Awaitable RPC is stuck/running | Use `rpc status`, then `rpc cancel`; verify the timeout is within `100-120000` ms. |
| Spawn fails | Add the requested prefab to `Resources`, attach the required network component, and enable Spawn. |
| `rpc expr` is unavailable | Expression compilation is unsupported on WebGL and IL2CPP; use `rpc named` or `rpc args`. |
| Terminal is not visible | Complete the startup popup, press backquote, and verify `Show Overlay` is enabled. |
| Types or modules disappear after an update | Align core and companion packages to exactly the same tag/commit, then run `Repair SDK Modules`. |

## Remove or Update

Use the main PlayServ SDK settings window to disable the module before selecting
`Remove Package`. Package removal affects the UPM dependency only. Imported
sample assets remain under `Assets/Samples` and can be removed separately when
they are no longer needed.

When updating the package, remember that Unity does not update an already
imported sample in place. Import the sample for the new package version and move
any project-specific prefab or script changes deliberately.

## Module Metadata

- Module ID: `debug-terminal`
- Capability: `debug.terminal`
- Minimum SDK version: `0.4.0`
- Runtime assembly: `Playserv.Runtime.Modules.DebugTerminal`
- Disable define: `PLAYSERV_MODULE_DISABLED_DEBUG_TERMINAL`
- Default state: disabled
