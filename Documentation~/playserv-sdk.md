# PlayServ Unity SDK

This document contains practical examples for the public runtime API exposed by:
- `Playserv.Wrapper.PlayServ`
- `Playserv.Wrapper.PlayServAuth`
- `Playserv.Wrapper.PlayServData`
- `Playserv.Wrapper.PlayServMatchmaking`
- `Playserv.Wrapper.PlayServCode`
- `Playserv.Wrapper.PlayServCatalog`
- `Playserv.Wrapper.PlayServStorefronts`
- `Playserv.Wrapper.PlayServStatus`
- `Playserv.Wrapper.PlayServEvents`
- `Playserv.Wrapper.PlayServAnalytics`
- `Playserv.Wrapper.PlayServRpc`
- `Playserv.Wrapper.PlayServSpawn`
- `Playserv.Wrapper.PlayServServerRpc`
- `Playserv.Wrapper.PlayServSettings`
- `Playserv.DataSubscription.ISharedEntity<T>`
- `Playserv.Spawn` components

See [Browser sign-in](browser-oauth.md) for an explicit browser login button,
account-switch semantics and platform requirements.

See [WebSocket room sessions](room-session.md) for a reusable Host/Join lifecycle
and an example game protocol.

See [Typed record references](record-references.md) for relation DTOs, canonical
loading, batch reads and authorization-context lifetime.

## Installation

### GitHub UPM (recommended)

The distribution repository contains the core package at its root and
all optional companion packages under `CompanionPackages~`. After configuring
GitHub SSH access for the operating-system account that runs Unity, open
`Window` -> `Package Manager` -> `Add package from git URL` and install the
prepared core package after its distribution tag has been published:

```text
git@github.com:playserv1/playserv-unity-sdk.git#0.6.5
```

Pin every PlayServ dependency to the same plain-SemVer distribution tag. A
companion package uses `?path` before the tag fragment:

```text
git@github.com:playserv1/playserv-unity-sdk.git?path=/CompanionPackages~/com.playserv.analytics#0.6.5
```

See the package [README](../README.md#install-from-github) for the complete
companion manifest and upgrade/downgrade instructions.

### UnityPackage / Assets import

When the SDK is imported under `Assets/playserv-unity-sdk`, Unity does not read this package's `package.json` dependencies. The SDK attempts to install `com.unity.nuget.newtonsoft-json` automatically in that mode. If Package Manager cannot install it, add this dependency manually to the game project's `Packages/manifest.json`:

```json
"com.unity.nuget.newtonsoft-json": "3.2.2"
```

### OpenUPM

Version **0.6.5 is prepared in this checkout**, not asserted to be available in
the registry. The examples below require its separate publication. Until then,
select an existing published release; the unpinned CLI installs a published version.

Install via OpenUPM CLI:

```bash
openupm add com.playserv.sdk
```

### Unity scoped registry (manual)

Add OpenUPM registry in your project `Packages/manifest.json`:

```json
{
  "scopedRegistries": [
    {
      "name": "OpenUPM",
      "url": "https://package.openupm.com",
      "scopes": [
        "com.playserv"
      ]
    }
  ],
  "dependencies": {
    "com.playserv.sdk": "0.6.5"
  }
}
```

## Versioning

- Package version is defined in `package.json` (`version`).
- Use Semantic Versioning: `MAJOR.MINOR.PATCH`.
- A source release tag uses `unity-<version>` (for example `unity-0.6.5`).
- Both tagged and manually dispatched releases require the source commit to be
  contained in `origin/dev` or an existing `origin/release/*` branch. The publisher
  refreshes these refs before checking; local-only or deleted branches do not qualify.
- The publisher writes a complete snapshot to distribution `main` and creates
  the matching plain-SemVer tag (for example `0.6.5`) atomically.
- Distribution tags are immutable and retain historical releases. Production
  UPM dependencies must use `#<version>` instead of following unpinned `main`.
- Core and every installed companion package must use the same version tag.
- `CHANGELOG.md` must include a heading for the same package version.
- The editor window displays the installed package version from Package Manager/package.json.
- Runtime SDK version constants are synchronized from `package.json` by `Tools/PlayServ/Internal/Sync SDK Version From package.json`.

The **SDK Version** card also provides registry update checks and an explicit,
confirmed update of core plus installed official companions (including Schema
Tool). The automatic check is project-cached for 24 hours; **Check for updates**
forces a refresh. No installation happens on window open. Local copies, forks
and incompatible package sets require manual resolution; package sources are
preserved. See [Editor update controls](../README.md#update-from-the-playserv-window)
for recovery, busy-state restrictions and first-install instructions. This is
Editor tooling only and does not change runtime configuration or credentials.

## Migrating legacy module APIs

SDK versions that predate the module-specific facades exposed optional
functionality through `PlayServ.*`. Open:

`Tools > PlayServ > Migrate Project`

The migration window scans project scripts under `Assets`, previews every
syntax-level replacement, and lets you apply all or only selected changes.
Generated scripts, comments, string literals, package source, and unrelated
types named `PlayServ` are not modified.

Typical replacements include:

```csharp
PlayServ.Invoke(...)       -> PlayServRpc.Invoke(...)
PlayServ.Subscribe<T>()    -> PlayServEvents.Subscribe<T>()
PlayServ.Spawn(...)        -> PlayServSpawn.Spawn(...)
PlayServ.SelectEntity(...) -> PlayServData.SelectEntity(...)
```

Before changing a script, the tool verifies that it still matches the scanned
version. Originals are backed up under `Library/PlayServ/ApiMigrationBackups`,
and the latest Markdown report is written to
`Library/PlayServ/Reports/PlayServApiMigrationReport.md`.

## Server SDK

Server/shared runtime build instructions are documented in
[server-sdk.md](server-sdk.md).
Unity package export and OpenUPM export do not build the server/shared runtime assembly.

## Module package manifest

Every optional runtime module can declare itself with a `module.playserv.json` file.
The file may live anywhere inside an Assets or UPM package. Asset paths in the
descriptor are resolved relative to the nearest package root containing
`package.json`.

```json
{
  "schemaVersion": 1,
  "id": "company-chat",
  "order": 200,
  "label": "Company Chat",
  "description": "Chat runtime and PlayServ facade integration.",
  "minSdkVersion": "0.3.0",
  "supportedPlatforms": [
    "Editor",
    "Standalone",
    "Android",
    "iOS",
    "WebGL"
  ],
  "conflictsWith": [],
  "capabilities": [
    "messaging.chat"
  ],
  "requiresPackages": [],
  "disableDefine": "PLAYSERV_MODULE_DISABLED_COMPANY_CHAT",
  "defaultEnabled": false,
  "isServerModule": false,
  "visibleInSettings": true,
  "visibleInExport": true,
  "assetPaths": [
    "Runtime"
  ],
  "dependencyIds": [
    "client-execution"
  ],
  "hiddenDependencyAssetPaths": [],
  "hiddenDependencyModuleIds": [],
  "profiles": [
    "client-sdk",
    "full-sdk"
  ],
  "rootAssemblyReference": "Company.PlayServ.Chat"
}
```

`schemaVersion` is currently `1`. `minSdkVersion` prevents a module from loading
under an older PlayServ SDK. `supportedPlatforms` accepts `Editor`,
`Standalone`, `Android`, `iOS`, and `WebGL`. `requiresPackages` contains exact
UPM package ids that must be installed before the module becomes available.

`id` and `disableDefine` must be unique. `dependencyIds` are user-visible
dependencies, while `hiddenDependencyModuleIds` are enabled automatically.
`conflictsWith` prevents incompatible modules from being enabled together.
`capabilities` declares stable feature identifiers that tooling or an
administration layer can discover without depending on assembly names.
`rootAssemblyReference` identifies the runtime assembly for validation and
dependency diagnostics.

PlayServ combines visible and hidden dependencies into one directed graph.
Dependencies are ordered before their dependents regardless of the UI `order`
value. Independent modules use `order` and then `id` as deterministic
tie-breakers. A cycle is rejected with a diagnostic containing the complete
dependency path. The resulting topological order is also written to the
project-generated runtime selection, so runtime module initialization follows
the same dependency order.

The package's `Playserv.Runtime.asmdef` is stable and is never rewritten for a
project.

Each runtime assembly registers its module with a `PlayServModuleAttribute`,
and server-side local execution uses `PlayServLocalExecutionFactoryAttribute`.
PlayServ discovers these assembly attributes at runtime, so a new module does
not require generated package code or changes to a central runtime registry.

Each optional runtime asmdef uses its module's disable define as a negative
`defineConstraint`. Disabling a module therefore excludes that assembly from
compilation. `Playserv.Wrapper.PlayServ` exposes only core connection and runtime
operations. Optional features are exposed exclusively by their module-specific
APIs, so disabling a module removes its public API assembly from compilation.

Use `Validate Modules` in `Tools/PlayServ/Settings` to check descriptor schema,
SDK compatibility, ids, cycles, topological order, conflicts, profiles, asset
paths, asmdef names, and assembly registrations.
Modules installed under `Packages/` are removed through Unity Package Manager;
the SDK's `Uninstall` action is reserved for modules imported under `Assets/`.

### Project module state

PlayServ stores the authoritative module selection in the consuming Unity
project:

```text
ProjectSettings/PlayServModules.json
```

Commit this file with the game project. It makes local editor imports, CI builds,
and other developers use the same SDK profile and module set.

```json
{
  "schemaVersion": 1,
  "activeProfileId": "client-sdk",
  "enabledModuleIds": [
    "client-execution",
    "data-subscription",
    "events",
    "spawn",
    "transport-websocket"
  ],
  "enabledEditorToolIds": [
    "codegen",
    "deployment",
    "model-sync"
  ],
  "knownModuleIds": [
    "apple-sign-in",
    "analytics",
    "client-execution",
    "client-rpc",
    "data-subscription",
    "events",
    "google-sign-in",
    "pulse",
    "rpc-core",
    "server",
    "spawn",
    "transport-rudp",
    "transport-udp",
    "transport-webrtc",
    "transport-websocket"
  ],
  "platformOverrides": [
    {
      "buildTargetGroup": "iOS",
      "profileId": "custom",
      "enabledModuleIds": [
        "apple-sign-in",
        "client-execution",
        "events",
        "transport-websocket"
      ]
    }
  ]
}
```

The base `enabledModuleIds` list is used when the current Unity
`BuildTargetGroup` has no override. A platform override is a complete module
selection for that target group. Create or clear the current platform override
from `SDK module settings > SDK profiles`.

Applying an SDK profile records its profile id. Manually changing a module marks
the current scope as `custom`. When a newly installed module is discovered,
PlayServ uses the recorded profile, or the module's `defaultEnabled` value for a
custom scope, to choose its initial state.

Module IDs that belong to a temporarily missing external package remain in the
project settings, so reinstalling that package restores its previous selection.

On the first SDK import, PlayServ migrates existing runtime module and editor-tool
preferences from `EditorPrefs`, writes `PlayServModules.json`, and removes the
legacy module preference keys. Module selection no longer depends on
`EditorPrefs`; per-user editor workflow and window preferences can still use it.

Changing the selected build target or pulling a modified
`PlayServModules.json` automatically synchronizes scripting defines, generated
project module selection, and assembly compilation. Use `Validate Modules` to
report an invalid schema, unknown profile, missing module package, duplicate
platform override, stale define state, or invalid assembly registration.

Project-specific module composition is generated only in the consuming project:

```text
Assets/PlayServ/Generated/Runtime/Playserv.Project.Generated.asmdef
Assets/PlayServ/Generated/Runtime/PlayServProjectModules.g.cs
```

`Playserv.Project.Generated` applies the selected module IDs during runtime
startup. Commit this generated directory together with
`ProjectSettings/PlayServModules.json` so CI and all developers compile the same
SDK composition.

No project-specific source is generated under the SDK package root. This applies
to packages installed from Git, a registry, or Unity's package cache, and also
keeps an SDK copied under `Assets/` immutable. Package reinstall or cache cleanup
cannot remove the project's generated module selection.

Schema DTOs and typed event extensions remain project-owned under
`Assets/Shared/Generated`. They may reference game types compiled into
`Assembly-CSharp`, so they intentionally remain outside the named module
composition assembly unless the game moves those types into its own asmdef.

## Companion package management

Open `Tools > PlayServ > Settings > SDK module settings` to install or remove
Apple Sign In, Google Sign In, Steam Auth, Epic Auth, Facebook Login,
Analytics, Pulse, Native Transports, WebRTC, and Game Server through Unity Package Manager.
Package state and module state are intentionally separate:

- `Install` adds the companion UPM package to the project.
- `Installed` means the package code is present in the project.
- Each `Enabled`/`Disabled` checkbox controls whether that module assembly is
  compiled. A package may own more than one module; Native Transports owns
  separate UDP and RUDP modules.
- `Remove Package` first disables all modules owned by the package, then removes
  the direct UPM dependency.

Git-installed companions use the same repository commit as the installed core
package. Local checkouts resolve companions from `CompanionPackages~`; scoped
registry installations request the matching package version.

The core package contains a committed generated companion catalog that maps each
package id to one or more module ids and a Git subfolder. Package-backed module
uninstall operations are routed through Unity Package Manager; the local asset
deletion path is used only for modules imported under `Assets`.

The `com.playserv.game-server` companion is dedicated to `UNITY_SERVER` builds
and accepts only `sk_*` credentials. It provides multi-room heartbeats, server
matchmaking, launch, room lifecycle, reservation admission and safe player
lookup without weakening the core player's credential policy. Configure it
through `PlayServGameServerOptions`; the default environment sources are
`PLAYSERV_API_URL` and `PLAYSERV_SERVER_KEY`. See
`CompanionPackages~/com.playserv.game-server/README.md`.
The facade also provides bounded server Analytics with per-event player
attribution and read-only typed Catalog/Storefront access using the same
rotating server credential.

## Schema workflows

The main PlayServ window separates schema work into two independent directions.

### Local contracts: C# to schema

`Local contracts` scans explicitly attributed project C# files and generates
the outputs configured in `playserv.schema.json`:

```text
C# [PlayServSchema] contracts
    -> canonical contract graph
    -> JSON Schema and optional backend C# DTOs
```

The external `com.playserv.schema-tool` companion owns this workflow.

### Server schema: schema to Unity C#

`Server schema` restores the Schema API workflow:

1. `Download Latest` sends the public `pk_*` Client Token to
   `POST /api/schemas/by-sdk-key`.
2. The response is saved as `Assets/Resources/latest-schema.json`.
3. The SDK compares it with `Assets/Resources/current-schema.json` by content
   hash and displays version, timestamp, and definition count.
4. `Apply & Generate C#` asks for confirmation, validates the complete schema,
   and generates models under `Assets/Shared/Generated/Models`.
5. Only after successful generation does the downloaded schema become
   `current-schema.json`.

Downloading never changes generated C# automatically. A malformed schema or
unsafe generated file name fails before the existing models are touched.
`Regenerate C# Models` can rebuild models when the downloaded and current
schemas already match.

The selected environment must provide a Schema API Server address, and
PlayServ Config must contain a public Client Token.

## External Schema Tool

`com.playserv.schema-tool` is delivered through Unity Package Manager but runs
outside the Unity managed process. It uses Unity's bundled .NET runtime, so a
developer does not need to install a system-wide .NET runtime. The same shipped
tool assembly supports every editor from Unity 2021.3 through Unity 6.6:
Unity 2021-2023 use the .NET 6 target directly, while Unity 6.x rolls it forward
to its bundled .NET 8 runtime.

### Install and initialize

1. Open `Tools > PlayServ > Settings`.
2. Expand `Schema Workflows`.
3. Under `Local contracts`, select `Install Local Schema Tool`.
4. Select `Initialize Local Schema`.
5. Commit `playserv.schema.json` and generated `playserv.schema.lock.json`.

For a Git installation, the package may also be declared directly:

```json
{
  "dependencies": {
    "com.playserv.sdk": "git@github.com:playserv1/playserv-unity-sdk.git#<revision>",
    "com.playserv.schema-tool": "git@github.com:playserv1/playserv-unity-sdk.git?path=/CompanionPackages~/com.playserv.schema-tool#<revision>"
  }
}
```

Use the same revision for core and Schema Tool.

### Declare contracts in C#

Only explicitly attributed types become schemas:

```csharp
using System;
using System.Collections.Generic;
using Playserv.Schema;

namespace Game.Contracts
{
    [PlayServSchema(
        "player.profile",
        Version = "1",
        Kind = PlayServSchemaKind.Entity,
        OwnedBy = PlayServSchemaOwner.Player,
        Read = PlayServSchemaReadPolicy.Owner,
        OnPlayerDelete = PlayServPlayerDeletePolicy.CascadeDelete,
        ClientRead = PlayServSchemaAccess.Allow,
        ClientWrite = PlayServSchemaAccess.Allow)]
    [PlayServFormerlyNamed("LegacyPlayerProfile")]
    public sealed class PlayerProfile
    {
        [PlayServField(
            "playerId",
            Required = PlayServRequiredMode.Required,
            Type = PlayServSchemaFieldType.Uuid,
            Primary = true,
            CodeKey = "player.profile.player-id")]
        public string Id { get; set; }

        public int Level { get; set; }
        public List<string> Tags { get; set; }
        public DateTime? LastSeenAt { get; set; }

        [PlayServIgnore]
        public string LocalOnlyCache { get; set; }
    }
}
```

- `PlayServSchema` sets the stable schema ID, kind, ownership/read lifecycle,
  ACL, description, singleton state, and optional version.
- `PlayServField` overrides the serialized name, stable code key, required,
  primary/unique/indexed flags, default value, target, and cardinality.
- `PlayServFormerlyNamed` records a previous type or field name for migrations.
- `PlayServIgnore` excludes a public member.

The analyzer parses C# with Roslyn. It does not load game assemblies or execute
project code.

### Configure sources and targets

Initialization creates `playserv.schema.json` in the project root:

```json
{
  "schemaVersion": 1,
  "projectId": "my-game",
  "sources": [
    {
      "id": "unity-client",
      "kind": "csharp",
      "paths": [
        "Assets/**/*.cs"
      ],
      "authority": "contract"
    }
  ],
  "targets": [
    {
      "kind": "json-schema",
      "output": "Assets/PlayServ/Generated/Schemas/playserv.schema.json"
    },
    {
      "kind": "csharp-contracts",
      "output": "../backend/Generated/PlayServContracts.g.cs",
      "namespace": "Game.Backend.Contracts"
    }
  ],
  "service": {
    "endpoint": "https://api.playserv.io",
    "environment": "dev",
    "projectId": "prj_...",
    "serverKeyEnvironmentVariable": "PLAYSERV_SERVER_KEY"
  }
}
```

Each C# source has its own `id`, glob paths, and authority. Generated output
paths are excluded from source discovery, so backend generation cannot feed
back into the client schema.

`json-schema` writes a deterministic JSON Schema 2020-12 document with stable
PlayServ metadata. `csharp-contracts` writes backend DTOs from the same canonical
contract graph. The lock file records tool/protocol versions and hashes for all
sources and outputs.

### Run from Unity, Rider, or CLI

The main SDK window keeps the primary `Generate Local Outputs` and `Analyze`
actions visible. `Advanced` provides:

- `Validate Generated`: fail when committed generated files are stale.
- `Start Watch`: run continuous external analysis independently of Unity.
- `Create IDE Launcher`: create `.playserv/bin/playserv-schema`.
- `Open Configuration`: reveal `playserv.schema.json`.

After creating the launcher:

```bash
.playserv/bin/playserv-schema analyze
.playserv/bin/playserv-schema generate
.playserv/bin/playserv-schema validate
.playserv/bin/playserv-schema push --dry-run
.playserv/bin/playserv-schema push
.playserv/bin/playserv-schema watch
```

Rider can call the launcher as an External Tool. A file watcher may use:

```text
Program:    $ProjectFileDir$/.playserv/bin/playserv-schema
Arguments:  generate --changed "$FilePath$"
Directory:  $ProjectFileDir$
```

The `--changed` path is an IDE optimization hint; correctness never depends on
the IDE sending it. Watch mode combines filesystem events with a lightweight
source-state check, covering editors that save through atomic file replacement.
Only one watcher may run for a project.

For CI, run `validate`. Exit code `0` means generated contracts match the source;
`1` means schema diagnostics or drift were found; `2` means configuration or
tool execution failed.

### Push to the Schema Service

The advanced Unity control **Push to PlayServ** and the CLI `push` command use
the shipped code-first Schema Service workflow:

1. Analyze attributed C# without loading game assemblies.
2. Exchange `PLAYSERV_SERVER_KEY` (or the configured environment-variable
   name) for a short-lived operator session through `/auth/cli`.
3. Read the current whole-schema `revision`.
4. Submit enums, parts, and entities together to `schema:push-from-code` with
   that revision as the precondition.

The backend applies the bundle atomically. A stale revision, validation error,
admin-authored name collision, or required migration leaves the existing
schema unchanged. Stable schema and field code keys preserve server IDs across
renames. Use `push --dry-run` to build and validate the bundle without reading
credentials or making a network request.

The server key is read only from the process environment. It is never accepted
as a CLI argument, written to `playserv.schema.json`, persisted, or logged.
`--endpoint` overrides `service.endpoint`; otherwise `PLAYSERV_API_URL` is used
as the final endpoint fallback. The existing `sync` command remains a
backward-compatible alias for local output reconciliation.

## SDK cache maintenance

PlayServ stores its cache schema and installed SDK version in
`Library/PlayServ/sdk-cache-state.json`. On a version or schema change, targeted
maintenance removes:

- `Library/SharedCodegen`;
- `Library/PlayServ/Cache`;
- stale PlayServ package directories under `Library/PackageCache`;
- `Assets/PlayServ/Generated/Runtime`, which is regenerated from the active
  module graph.

The current package cache and unrelated Unity caches are preserved. Use
`Tools > PlayServ > Cache > Clear PlayServ Cache` to run the targeted cleanup
manually. Use `Rebuild Project Library...` only when Unity's broader cache is
corrupted; it requires confirmation, closes Unity, deletes `Library`, and
reopens the project.

## Analytics module

The optional `com.playserv.analytics` companion package records explicit
gameplay events without Firebase or another analytics SDK. Install and enable
`Analytics` in `Tools > PlayServ > Settings > SDK module settings`. It depends
on `Client Execution` and is enabled by the Full SDK profile or explicitly by
the project.

Track events after `PlayServ.Connect()` succeeds:

```csharp
using System.Collections.Generic;
using Playserv.Wrapper;

PlayServAnalytics.SetUserProperty("role", "parent");
PlayServAnalytics.Track("auto_login");
PlayServAnalytics.Track("login_sso_success", "provider", "google");
PlayServAnalytics.Track(
    "match_finished",
    new Dictionary<string, object>
    {
        { "mode", "duo" },
        { "duration_seconds", 92.5f },
        { "score", 1250 },
        { "won", true }
    });
```

Supported parameter values are strings, booleans, integer numeric types, and
finite floating-point or decimal numbers. An event can contain up to 50
parameters. Event names are limited to 128 characters; parameter and user
property keys are limited to 64 characters.

Each event receives:

- a unique event ID and monotonically increasing session sequence;
- UTC timestamp;
- PlayServ user ID, or an explicit ID set with `SetUserId`;
- a per-connection-module session ID;
- SDK version, application version, and Unity platform;
- a snapshot of the current user properties.

Use an explicit user ID only when it is the same verified identity used by the
PlayServ connection:

```csharp
PlayServAnalytics.SetUserId(playerId);
PlayServAnalytics.SetUserProperty("subscription", "premium");
PlayServAnalytics.RemoveUserProperty("subscription");
PlayServAnalytics.ClearUserProperties();
```

### Queue and delivery

The client keeps at most 500 events in memory, sends batches of up to 20, and
attempts a flush every 10 seconds, when a batch fills, and after reconnect.
Failed batches remain queued for a later attempt. When the queue is full, the
oldest event is dropped and a warning is logged.

Use `FlushAsync` before a controlled logout or scene/application shutdown:

```csharp
await PlayServAnalytics.FlushAsync(cancellationToken);
```

The queue is currently memory-only. A process crash or forced application exit
can therefore lose pending events. Durable disk-backed delivery can be added
later without changing the public tracking API.

Collection can be disabled for consent or privacy settings:

```csharp
PlayServAnalytics.SetCollectionEnabled(false);
```

Disabling collection immediately clears pending events and ignores new events
until it is enabled again. The module does not automatically collect device
identifiers, advertising IDs, email addresses, or arbitrary object fields.

### Migrating eggie-crush from Firebase Analytics

The existing eggie-crush `AnalyticService` maps directly:

```csharp
// Firebase wrapper
analyticService.SentEvent("auto_login");
analyticService.SentEvent("login_sso_success", "provider", providerName);

// PlayServ
PlayServAnalytics.Track("auto_login");
PlayServAnalytics.Track("login_sso_success", "provider", providerName);
```

Dictionary calls use the same string/object shape, but unsupported values now
fail early instead of being silently ignored:

```csharp
PlayServAnalytics.Track(eventName, eventParameters);
```

Remove the Firebase Analytics package only after all direct
`FirebaseAnalytics.LogEvent` calls have been migrated and the PlayServ backend
ingestion endpoint has been verified in the target environment.

### Provider and backend contract

By default, the module maps each bounded batch to the shipped
`POST /analytics/events` runtime endpoint. The public `pk_*` token identifies
the project and environment; when a managed or custom player token is
available, the request also includes it so the backend can attribute the
caller. Event parameters and user properties are preserved inside the event
payload together with SDK, app, platform, session, sequence and batch context.

Route events to another destination by implementing
`IPlayServAnalyticsProvider`. The implementation belongs to the client project,
for example under `Assets/Analytics`; it is not shipped by the PlayServ SDK:

```csharp
using System.Threading;
using System.Threading.Tasks;
using Playserv.Analytics;

public sealed class ProjectAnalyticsProvider : IPlayServAnalyticsProvider
{
    public bool IsReady => true;

    public Task SendAsync(
        PlayServAnalyticsBatch batch,
        CancellationToken cancellationToken = default)
    {
        return analyticsApi.SendAsync(batch, cancellationToken);
    }
}
```

Register it during client startup. Registration is allowed before
`PlayServ.Connect()` and remains active when the PlayServ runtime reconnects:

```csharp
PlayServAnalytics.SetProvider(new ProjectAnalyticsProvider());
```

Return to the built-in HTTP ingestion provider when needed:

```csharp
if (PlayServAnalytics.HasCustomProvider)
    PlayServAnalytics.ResetProvider();
```

For a Firebase migration, the client project can implement the same interface
with `FirebaseAnalytics.LogEvent(...)` inside `SendAsync`. This keeps PlayServ's
event validation, context, consent, queue, and batching while Firebase remains
an optional client dependency. PlayServ does not compile, install, or select a
Firebase provider automatically.

Provider failures are propagated by manual `FlushAsync` calls and logged by
background flushes. In both cases the unsent events remain queued.

## Player authentication

### Optional display names

Before configuring a managed session, set `settings.AnonymousDisplayName = chosenName`
to name a **new** anonymous player. This runtime-only setting also applies when the
existing lifecycle creates a replacement anonymous player (for example after logout).
It is not saved to config assets or the credential store, and changing it does not
rename a restored player or force creation of another account.

For provider login/link, choose a name explicitly on the immutable proof:

```csharp
var proof = PlayServExternalIdentityProof
    .FromGoogleIdToken(googleIdToken, expectedNonce)
    .WithDisplayName(chosenName);
var login = await PlayServAuth.LoginExternalAsync(proof);
// Alternatively, explicitly link this proof to the current player:
// var linked = await PlayServAuth.LinkIdentityAsync(proof);
```

Google/Apple companions do **not** copy `DisplayName`/`FullName` automatically. After
`TryCreateBackendProof`, the game may explicitly call `.WithDisplayName(...)` with
a consented provider name or its own nickname. Existing proof factories remain unchanged.

The SDK trims names, truncates to 64 UTF-16 code units without splitting an emoji's
surrogate pair, trims any newly exposed trailing whitespace, and omits blank names.
It sends exactly `display_name` on `/auth/players/anon`, `/login` and `/link` only.
The name is cosmetic, not authenticated identity; it is not sent on merge or refresh.
The standard Unity HTTP module supports it. A custom module should implement the
additive `IPlayServAnonymousLoginHttpClient` and serialize the new login/link DTO field;
legacy anonymous modules continue signing in without a name with one warning per session.

This is **not a rename API**. The backend applies names on player creation/first
provider link (first-link-wins); an already-linked returning login does not fill an
empty name or overwrite an existing one. A name set during anonymous creation may
therefore remain after provider linking. Do not optimistically set the profile from
the requested name; read the authoritative value with
`await PlayServAuth.RefreshCurrentPlayerProfileAsync()`.

The request-shape and lifecycle tests use local fixtures, not live backend integration.

### Environment client tokens

In the standard package Editor configuration, Dev and Prod have independent Client
Token values. Tokens live in local `EditorPrefs`, scoped by project path, config
asset GUID and environment; the selected environment is project-local too. The
PlayServ window and config Inspector use this store. `config.ClientToken` and
`config.ToSettings()` resolve the active token in the Editor without serializing
it into the project asset. Explicit `PlayServSettings` and custom project settings
providers retain their existing behavior. Endpoint switching is unchanged.

On first use, the existing serialized token is moved to the currently selected
environment and cleared from the asset. An existing local value is never
overwritten. The old global environment preference is used only to initialize the
project preference. Verify that the migrated token belongs to that environment:
the SDK cannot infer its intended environment or recover a token overwritten by
an earlier edit. Other environments start empty, and clearing a token remains
effective after reload. A new checkout path or another developer must configure
its own tokens. Migration does not remove values from existing Git history.

Only public `pk_*` keys belong here, never server keys or player JWTs. Local
preferences are not an encrypted secret store. Switching environments affects the
next SDK configuration, not an existing connection or authenticated session.

For player builds, the SDK validates and temporarily bakes the active public token
into configured managed config assets. A missing token blocks builds of the default
runtime `Resources/PlayServConfig` (including nested Resources locations); blank
unrelated template configs do not block a build. It restores only the injected token field afterwards,
preserving other build-tool changes. Recovery also runs when the Editor becomes
idle or quits and after reopening an interrupted build, using a journal under
`Library/PlayServ`. Do not delete `Library` before recovery or commit an in-progress
build snapshot. A conflicting edit to the token is preserved and reported rather
than silently overwritten. Only the active key is baked; the inactive local key
is not included. The baked key remains readable by users of the built game.

For CI, set **both** `PLAYSERV_ENVIRONMENT` (for example `Dev` or `Prod`) and
`PLAYSERV_CLIENT_TOKEN` in the Unity process environment. They override local
selection/token for that process without modifying the local store. Supplying
only one, an unknown environment, an empty token or a non-public credential fails
with a safe error. No token is echoed in diagnostics. These token-selection
variables do not change explicit endpoint settings; continue configuring the
build's endpoints through the existing game build setup. Projects using custom
settings providers remain responsible for their own build configuration.

### Managed sessions

When `PlayServSettings` contains a public `pk_*` client token and does not
contain `PlayerAccessToken` or an application-supplied `RuntimeTokenProvider`,
`PlayServ.Connect()` automatically creates or restores a managed anonymous
player. The access token stays in memory; the player ID and rotated refresh
token are stored through `IPlayServPlayerSessionStore`.

Current state is available without exposing either token:

```csharp
bool authenticated = PlayServAuth.IsLoggedIn;
string playerId = PlayServAuth.PlayerId;
PlayServSessionKind kind = PlayServAuth.SessionKind;
PlayServSessionInfo session = PlayServAuth.CurrentSession;
IReadOnlyList<string> linkedProviders = session.LinkedProviders;
```

After an identity provider returns an ID token, verify it through the PlayServ
backend and preserve the current anonymous player by default:

```csharp
using Playserv.Identity;
using Playserv.Wrapper;

var proof = PlayServExternalIdentityProof.FromGoogleIdToken(
    googleIdToken,
    expectedNonce);

PlayServAuthResult result = await PlayServAuth.LoginExternalAsync(proof);
if (result.IsSuccess)
{
    UnityEngine.Debug.Log($"Player {result.Session.PlayerId} is registered.");
}
else if (result.Conflict != null)
{
    UnityEngine.Debug.LogWarning(
        $"{result.Conflict.ProviderId} belongs to " +
        result.Conflict.Conflicting.PlayerId);
}
else
{
    UnityEngine.Debug.LogError(result.Error.ToString());
}
```

`PreserveCurrentPlayer` sends the current player JWT and keeps the current
`plr_*` while linking the provider. A `409 provider_already_linked` result is
returned as `PlayServAuthConflict` and does not replace the current player.
Use `RecoverProviderAccount` when signing in on another installation and the
provider-owned player should replace the local anonymous player:

```csharp
var result = await PlayServAuth.LoginExternalAsync(
    proof,
    PlayServExternalLoginMode.RecoverProviderAccount);
```

Discover available providers and manage additional identities through the same
managed session:

```csharp
PlayServAuthProvidersResult providers = await PlayServAuth.GetProvidersAsync();
foreach (PlayServAuthProviderInfo provider in providers.Providers)
    UnityEngine.Debug.Log($"{provider.Label}: available={provider.Available}");

PlayServAuthResult link = await PlayServAuth.LinkIdentityAsync(proof);
if (link.Conflict?.Current != null && link.Conflict.Conflicting != null)
{
    // The proof belongs to another player. The UI must ask which player to keep.
    PlayServAuthResult merge = await PlayServAuth.MergeIdentityAsync(
        link.Conflict,
        PlayServMergeChoice.KeepCurrentPlayer,
        proof); // obtain a fresh provider proof before a real merge
}

PlayServAuthResult unlink = await PlayServAuth.UnlinkIdentityAsync("google");
```

Load the signed-in player's safe runtime profile through the same auth facade:

```csharp
PlayServPlayerProfileResult profileResult =
    await PlayServAuth.GetCurrentPlayerProfileAsync();
if (profileResult.IsSuccess)
{
    Debug.Log(profileResult.Profile.Name);
    Debug.Log(string.Join(", ", profileResult.Profile.LinkedProviders));
}

// Force a backend read instead of returning the cached successful profile.
profileResult = await PlayServAuth.RefreshCurrentPlayerProfileAsync();
```

This client route can only read the JWT player's own row. The DTO deliberately
excludes IP, fingerprint, moderation and merge-forensics fields.

Merge is identity-only: records owned by the absorbed player are not moved.
`MergeIdentityAsync` accepts only a typed provider conflict, validates that it
still belongs to the current managed player, and derives the primary/absorbed
IDs from `PlayServMergeChoice`. A merge provider overlap is returned as
`PlayServAuthConflictKind.MergeProviderConflict` with `ConflictingProviders`.

`PlayServAuthResult.IdentityMutationState` distinguishes confirmed remote
application from a rejected operation or an outcome that became unknowable due
to a network failure. In particular, a merge may commit and then return `403`
when the primary becomes banned; the SDK probes the current refresh credential,
emits `SessionLost(Banned)` when confirmed, and otherwise reports
`MergeOutcomeUnknown`.

The built-in token-proof helpers cover Apple/Google ID tokens, Facebook Limited
Login, Epic external auth tokens (including launcher exchange-code mode), Steam
tickets, and PlayServ custom tokens. Provider discovery also preserves entries
without a token helper, such as `playserv-webhook`; `SupportsProviderToken`
indicates whether this SDK can construct a native proof. Browser OAuth/PKCE is
not performed by the Unity SDK in this release.

Automatic player fingerprinting is enabled by default on Android and iOS. The
SDK hashes Unity's platform-scoped device identifier together with the platform
and `Application.identifier`, then sends only the lowercase SHA-256 digest as
`stable.device_id_hash`. The source identifier is never stored, logged, or sent.
Disable automatic collection explicitly when your title's privacy policy
requires it:

```csharp
settings.EnableAutomaticPlayerFingerprint = false;
```

Desktop, console, WebGL, and games with their own consent flow can configure a
runtime-only `IPlayServPlayerFingerprintProvider`. An explicit provider always
takes precedence, including when it returns `null`:

```csharp
settings.PlayerFingerprintProvider = new MyFingerprintProvider();

// Returned by MyFingerprintProvider.GetFingerprintAsync(ct):
var fingerprint = new PlayServPlayerFingerprint(
    new Dictionary<string, object> { ["platform"] = "windows" },
    new Dictionary<string, object>
    {
        ["app_version"] = Application.version,
        ["locale"] = "uk-UA"
    });
```

The fingerprint is sent for anonymous minting and both provider-login modes.
`PreserveCurrentPlayer` reuses one collected snapshot for an anonymous bootstrap
and the following provider login. Refresh, link, unlink, merge, and sign-out do
not add fingerprint fields. Unsupported automatic platforms continue without a
fingerprint and emit one Editor/Development Build warning. The validator rejects
raw device IDs and identifiers forbidden by the runtime contract, including
advertising IDs, hardware serials, IMEI/IMSI, MAC addresses, and WebGL
fingerprinting signals. Fingerprints remain ban/anti-abuse signals only: they
never select or restore a player session.

On Android the source value depends on the app signing key; debug, locally
signed, and Google Play-signed builds may therefore produce different hashes.
On iOS the source is identifier-for-vendor and follows its platform lifecycle.

Successful login creates a new backend session, so an online built-in transport
is disconnected and reconnected with the new authorization. Ordinary access
token refresh remains in-place and does not reconnect.

Explicit logout attempts to revoke the current refresh token, always removes
the local managed session, creates a new anonymous player, and reconnects if the
transport was online:

```csharp
PlayServAuthResult logout = await PlayServAuth.LogoutAsync();
// A failed remote revoke is reported in logout.Error; logout.Session still
// describes the fresh anonymous player when local replacement succeeded.
```

Terminal server and refresh failures stop automatic refresh/reconnect and raise
one session-loss notification. Explicit logout does not raise this event:

```csharp
PlayServAuth.SessionLost += info =>
{
    UnityEngine.Debug.LogWarning(
        $"Session lost: {info.Reason}; player={info.PreviousSession.PlayerId}");
};
```

Network, HTTP, persistence, conflict, and transport-reconnect failures are
returned through `PlayServAuthResult`. Cancellation, null arguments, and calls
made while the transport is connecting/handshaking/reconnecting remain
exceptions. HTTP failures expose `HttpStatus`, `BackendCode`, `BackendTitle`,
and `BackendDetail` on `PlayServAuthError`. When the application supplies `RuntimeTokenProvider` or
`PlayerAccessToken`, `SessionKind` is `Unmanaged`; managed login/logout refuses
to overwrite that configuration. Provider discovery remains available because
it only requires the public project client token.

The default `PlayerPrefs` store remains compatible with sessions written by
0.3.7; records without a kind are treated as anonymous. Because PlayerPrefs is
not secure credential storage, Editor and Development builds warn once when a
registered refresh token is saved. Production games should provide a
platform-secure `IPlayServPlayerSessionStore`. Tokens are never written to SDK
logs.

## Unified errors

Core runtime error surfaces expose a common immutable `PlayServError` alongside
their existing domain-specific error types. `Code` is a stable cross-module category;
`SourceCode` retains the exact Problem Details, auth, RPC, or protocol code.
`HttpStatus` and `TransportCode` identify the originating plane, while
`Retryable` applies one policy to network failures, timeouts, `408`, `425`,
`429`, `5xx`, and backend command errors marked retryable.

```csharp
PlayServ.OnError += error =>
    UnityEngine.Debug.LogError($"{error.Code}/{error.SourceCode}: {error.Message}");

PlayServAuthResult login = await PlayServAuth.LoginExternalAsync(proof);
if (!login.IsSuccess && login.UnifiedError?.Retryable == true)
    ScheduleLoginRetry();
```

`TransportError`, `PlayServAuthError`, `PlayServRpcError`,
`PlayServDataException`, and `DataSubscriptionException` remain available and
provide `UnifiedError` for source compatibility. Correlated auth/data/RPC
failures stay on their operation result or exception and are not duplicated on
the global event. `RawDetails` is never logged automatically, and credential-like
JSON fields, bearer values, and JWT-shaped strings are redacted centrally.

## Typed V2 records

`PlayServData.Records<T>()` resolves `typeof(T).Name` through the runtime table
catalogue and caches its `ent_*` ID. Use `Records<T>("ent_...")` when the CLR
name differs from the schema name. Existing subscription methods on
`PlayServData` are unchanged.

The same cache is public through `PlayServData.GetTablesAsync()`,
`GetTableAsync(idOrName)`, and `RefreshTablesAsync()`. Each
`PlayServDataTableInfo` contains the entity ID/name/description, singleton flag,
row count, updated timestamp, read policy, and complete client/server/backend
capabilities. A missing lookup throws `PlayServDataTableNotFoundException`.

```csharp
using System.Linq;
using Playserv.Data;
using Playserv.DataSubscription;
using Playserv.Schema;
using Playserv.Serialization;
using Playserv.Wrapper;

public sealed class InventoryItem
{
    [PlayServField("code")]
    public string Code { get; set; }

    [PlayServJsonName("display_name")]
    public string DisplayName { get; set; }

    public int Durability { get; set; }

    [PlayServField("owner_profile")]
    public InventoryOwner OwnerProfile { get; set; }

    [PlayServIgnore]
    public string LocalUiState { get; set; }
}

public sealed class InventoryOwner
{
    public string DisplayName { get; set; }
}

var items = PlayServData.Records<InventoryItem>();
PlayServRecord<InventoryItem> created = await items.CreateAsync(
    new InventoryItem { Code = "starter-sword", DisplayName = "Sword", Durability = 100 });

PlayServRecord<InventoryItem> loaded = await items.LoadAsync(created.Id);
loaded.Value.Durability--;
await loaded.SaveAsync();
```

### Retrying record creation

`CreateAsync` and `BulkCreateAsync` accept an optional `idempotencyKey` after `ct`.
Keep a key for each logical operation and reuse it if its response is lost:

```csharp
// Keep these in game-owned operation state, outside any retry loop.
string operationKey = System.Guid.NewGuid().ToString("D");
var reward = new InventoryItem { Code = "reward-sword", DisplayName = "Sword", Durability = 100 };

// If the outcome is unknown, explicitly repeat this call with the SAME key and fields.
var createdReward = await items.CreateAsync(reward, ct: ct, idempotencyKey: operationKey);

// A different logical operation needs a different key. A bulk key covers the entire
// atomic batch (1-200 records), not individual rows or a client-side fan-out.
string batchKey = System.Guid.NewGuid().ToString("D");
var rewards = new[] { reward };
var batch = await items.BulkCreateAsync(rewards, ct: ct, idempotencyKey: batchKey);
```

Reuse requires identical serialized fields, and for bulk creation the same row order
and defaults, against the same table and caller context (project/environment and
credentials/player identity), within the backend's idempotency retention window.
An already completed request replays its original result; a changed payload with the
same key produces the existing typed `409 conflict`. A still-running request can also
return a conflict, so idempotency does not make every immediate retry succeed.
Replay is not a reload of the record's latest state.

Omitting the key or passing `null` generates a new UUID for each call, preserving the
previous behavior. Explicit keys must be non-blank, contain no control characters and
have at most 128 characters; invalid keys fail before catalogue or mutation HTTP.
The SDK neither persists keys nor automatically retries writes. Cancellation stops
waiting, not an accepted server write. These APIs also work on Game Server Records
and `AsPlayer(...)`; natural-key upserts and `LoadOrCreateAsync` are unchanged.

The table catalogue also supplies advisory ACL capabilities:

```csharp
PlayServDataCapabilities capabilities = await items.GetCapabilitiesAsync();
if (capabilities.IsKnown)
{
    ShowReadUi(capabilities.CanRead);
    ShowWriteUi(capabilities.CanWrite);
}

// Re-fetch after changing schema ACL in the dashboard.
capabilities = await items.RefreshCapabilitiesAsync();
```

`Capabilities.Client` is the authority used by the Unity player SDK;
`Server` and `Backend` expose the other advertised subjects for diagnostics.
Known client denials are refreshed once and rejected before the record or
realtime request. Older/custom backends that omit `acl` produce `Unknown` and
are never blocked locally. These checks are advisory: the backend remains
authoritative, and `CanWrite` does not bypass player ownership, record
visibility, validation, or ETag rules. Backend and local ACL refusals use
`PlayServDataAccessDeniedException`; `WasRejectedLocally` distinguishes them.

The backend, not the client model, mints the `rec_*` ID. Each handle stores its
flattened value, timestamps, optional player owner, canonical JSON snapshot and
ETag. `HasPendingChanges` compares the current top-level value to that snapshot.
`SaveAsync` sends only changed fields as JSON Merge Patch, including explicit
`null`, and advances snapshot/ETag only after a successful response. Stale
`If-Match` returns `PlayServRecordConflictException` with
`Kind == StaleVersion`.

Each save captures its patch before asynchronous access checks and HTTP. Changes to
serialized fields made while it is pending are preserved over the successful server
response and remain visible through `HasPendingChanges`. The snapshot, ETag and
timestamps still advance to the acknowledged server state; a subsequent (including
already queued) save sends the remaining changes using the new ETag.

This reconciliation uses top-level fields, just like Save: nested objects and arrays
are retained as whole fields when edited during the request. Server-normalized fields
without newer local edits are accepted. `Value` can still be replaced, so read it from
the handle after awaiting Save rather than retaining an old DTO reference. Modify DTOs
on the owning Unity context; arbitrary simultaneous multi-threaded mutation is not supported.
Failed or cancelled saves leave the existing snapshot/ETag and local edits unchanged.
Singleton saves use the same behavior. Explicit reload and realtime synchronization
continue to use backend-wins semantics and may intentionally replace pending edits.

Use selectors or string fields for dynamic schemas:

```csharp
var allowedCodes = new[] { "starter-sword", "starter-shield" };
var query = new PlayServRecordQuery<InventoryItem>()
    .Where(x => x.Durability >= 10 && x.DisplayName != null)
    .And(x => allowedCodes.Contains(x.Code))
    .OrderBy(x => x.Code)
    .Search("sword")
    .SelectFields(x => x.Code, x => x.DisplayName, x => x.Durability)
    .Include(x => x.OwnerProfile)
    .WithLimit(50);

PlayServRecordPage<InventoryItem> page = await items.QueryAsync(query);
string next = page.NextCursor;
string previous = page.PreviousCursor;
```

Selectors use the same `PlayServJsonName` / `PlayServField` mapping as runtime
serialization; `PlayServIgnore` members cannot be selected or serialized.
`PlayServLoadOptions` supports `Fields` and `Expand`. A projected load or query
with selected/hidden fields produces `IsPartial == true`; saving it is rejected
until a full `ReloadAsync()` prevents omitted fields from becoming accidental
deletions.

`Where(x => ...)` accepts `==`, `!=`, `>`, `>=`, `<`, `<=`, null equality,
captured collection `Contains`, and predicates joined with `&&`. Repeated
`Where` and `And` clauses are also combined with AND. `||`, field-to-field
comparisons, arithmetic, and arbitrary method calls are rejected locally.
`SelectFields` and `Hide` are mutually exclusive.

### Read a saved View

Use a known View ID created in the administrative interface. Filters, sorting and
hidden columns come entirely from the saved View; this API does not accept query
overrides, create/edit Views, or open realtime View subscriptions.

```csharp
var viewPage = await items.QueryViewAsync("view_inventory", ct: cancellationToken);
if (viewPage.HasMore)
    viewPage = await items.QueryViewAsync("view_inventory",
        new PlayServPagination(viewPage.NextCursor, limit: 50), cancellationToken);
```

This uses `GET /data/tables/{entityId}/records?view_id=...`, resolving the table
through the same catalogue/ACL cache as ordinary Records reads. Default page size
is 50, supported range 1–200. View IDs and cursors are URL-encoded and the server's
record order is preserved. Changing a saved View between requests can change later
pages; the SDK does not freeze its definition.

Every returned record has `IsPartial = true`, even if its response happens to
contain every field: the backend does not expose the hidden-column list. There is
no automatic hydration. Reload explicitly **before editing**, then save normally:

```csharp
var itemFromView = viewPage.Records[0]; // Check Records.Count first in game code.
await itemFromView.ReloadAsync(cancellationToken);
itemFromView.Value.DisplayName = "New name";
await itemFromView.SaveAsync(cancellationToken);
```

Saving a partial handle is rejected locally. Reload reads the full record with
its current ETag and can itself be denied by backend access rules. Type-name and
explicit entity-ID sets work on client, Game Server and `AsPlayer(jwt)` facades.
Server/acting View reads remain server-authorized, without `X-Acting-Player`.
Backend not-found, ACL, cursor and query-limit failures use the existing Records
exceptions and source codes; there is no invented View-specific not-found code.

### Query hydration

The current REST query route does not accept `fields` or `expand`. When a query
uses `SelectFields` or `Include`, the SDK first obtains the page and then runs up
to four record GET requests concurrently with those options. Ordering and
cursor metadata come from the original page, ETags come from the hydrated GETs,
and a hydration failure fails the whole call instead of returning mixed handles.

### Record realtime synchronization

A fully loaded record can also own a keyed realtime subscription:

```csharp
IPlayServRecordSubscription<InventoryItem> recordLive =
    await loaded.SubscribeAsync();

recordLive.Changed += change =>
    RenderChangedFields(change.Record, change.ChangedFields);
recordLive.Conflict += conflict =>
    ShowBackendWon(conflict.LocalValue, conflict.RemoteValue);
recordLive.SynchronizationError += error =>
    UnityEngine.Debug.LogError(error.UnifiedError.ToString());

// Completes after the canonical REST reload updates Value, snapshot and ETag.
await recordLive.RefreshAsync(cancellationToken);
```

The dataflow snapshot contains record fields but not REST metadata. For that
reason each changed push triggers a canonical `GET /records/{id}` before the
handle is mutated. `Value`, the canonical snapshot, `UpdatedAt`, owner, and
`ETag` therefore move to one authoritative version, and repeated push frames
are coalesced. Partial handles are fully reloaded before the transport opens;
handles with existing unsaved changes must first call `SaveAsync` or
`ReloadAsync`.

Edits made after subscription are allowed. If a remote update arrives while
`HasPendingChanges` is true, synchronization uses backend-wins semantics:
`Conflict` is raised first with a clone of the local value, the applied remote
value, and local/remote top-level wire-field changes; `Changed` follows with
`OverwrotePendingChanges == true`. A server deletion or terminal target-not-
found frame sets `IsDeleted`, retains the last valid value, emits the standard
failure sequence once, and leaves the handle in `Terminated` state. Explicit
`CloseAsync`/`Dispose`, refcounting, reconnect replay, and main-thread event
delivery reuse the normal managed-subscription lifecycle.

`ISharedCollection<T>`, `IPlayServRecordSubscription<T>`, and the legacy
`ISharedEntity<T>` implement `IPlayServRefreshableSubscription`. Explicit
`RefreshAsync(ct)` uses the handle's current server ID, including the remapped ID
after reconnect. It does not add a backend reference or alter local refcounts.
Cancellation stops waiting and leaves the handle active; an already-sent request
cannot be retracted, so a late snapshot can still arrive through the normal push
path. Closed and terminated handles reject refresh locally.

Use the same query type to open a native realtime collection:

```csharp
var liveQuery = new PlayServRecordQuery<InventoryItem>()
    .Where(x => x.Durability, PlayServQueryOperator.LessThan, 25)
    .SelectFields(x => x.Code, x => x.DisplayName, x => x.Durability)
    .Include(x => x.OwnerProfile)
    .WithLimit(50);

ISharedCollection<InventoryItem> live = await items.SubscribeAsync(liveQuery);
live.Changed += current => RenderInventory(current);
live.Failure += error => UnityEngine.Debug.LogError(error.ToString());

// Request a correlated full snapshot without replacing the subscription handle.
await live.RefreshAsync(cancellationToken);

// CloseAsync waits for DataSubscriptionCloseResponse. Dispose performs the
// same close as best effort when a result is not needed.
PlayServSubscriptionCloseResult close = await live.CloseAsync();
```

Identical realtime queries share one server subscription and are reference
counted locally; only the final handle sends `DataSubscriptionCloseRequest`.
Active handles are replayed after reconnect and rebound to the new server ID.
Backend-restored references are released once so reconnect does not leak an
extra refcount. A deleted target or server `49001` moves every matching handle
to `Terminated`, raises `Failure`/the legacy `Error`, then `Terminated`, and is
not replayed again. `Value`/`Items` retain their last valid snapshot.

| Query feature | REST | Realtime transport |
| --- | --- | --- |
| `Eq/Neq`, `GT/GTE`, `LT/LTE`, string operators, AND | Yes | Yes |
| OR groups (up to 8, each group is AND) | No | Yes |
| Limit and positive field projection | Yes | Yes |
| Direct `Include` | Yes, hydrated through record GET | Yes, nested selection |
| Nested `Include` (up to 6 relation levels) | No | Yes |
| `In/Nin`, null/empty/between/count checks | Yes | No |
| Search, sort, cursor, `Hide` | Yes | No |

Unsupported realtime features throw `PlayServQueryCapabilityException` before
the transport request and list every rejected feature. There is no client-side
filtering, sorting, or REST re-query fallback. The raw `SelectCollection` API is
still available for hand-written dataflow queries.

Realtime OR values use the same expression parser and are always sent through
dataflow variables. Common filters apply to every alternative, and nested paths
use the wire name of every relation segment:

```csharp
var regionalGuilds = new PlayServRecordQuery<InventoryItem>()
    .Where(x => x.Durability > 0)
    .Or(
        x => x.Region == "eu",
        x => x.Region == "us" && x.Durability >= 10)
    .Include(x => x.Guild.Owner)
    .WithLimit(50);

ISharedCollection<InventoryItem> live =
    await items.SubscribeAsync(regionalGuilds, cancellationToken);
```

Using OR or a nested include with `QueryAsync` is rejected as a REST capability
error before table catalogue or record network I/O. Direct REST includes remain
available through bounded record hydration.

Legacy keyed `ISharedEntityBuilder<T>.Include` and `Select` now affect the
selection set. Its `Where` method remains for source compatibility but is
deprecated: keyed backend reads do not apply filters, so `BindAsync` throws a
capability exception instead of silently ignoring the predicate. Use
`Records<T>().SubscribeAsync(...)` for filtered collections.

Natural keys are primitive schema fields declared `unique` or `primary`:

```csharp
PlayServLoadOrCreateResult<InventoryItem> result =
    await items.LoadOrCreateAsync(
        PlayServNaturalKey<InventoryItem>.For(x => x.Code, "starter-sword"),
        () => new InventoryItem { Code = "starter-sword", Durability = 100 });
```

The SDK resolves natural keys through the backend's indexed
`records:by-natural-key` endpoint, and
the factory runs only when that query is empty. After create, the returned
server-minted ID is reloaded before the handle is exposed. If the ID was not
persisted because another client won the unique-key race, the SDK repeats the
query and returns the competing handle with `WasCreated == false`. If neither
record exists it reports `created_record_not_persisted`; multiple query matches
report `natural_key_not_unique`. This recovery protects Unity callers from a
phantom handle, but it is not an atomic backend operation and cannot undo
server-side notifications or metadata effects from a rejected insert.

Network/auth/validation failures use `PlayServDataException`, missing records
use `PlayServRecordNotFoundException`, and structured `409`/`412` failures use
`PlayServRecordConflictException`; cancellation and invalid API use remain
ordinary exceptions.

Bulk convenience methods are implemented entirely by the SDK over the existing
runtime endpoints:

```csharp
PlayServLoadAllResult<InventoryItem> all = await items.LoadAllAsync(
    new PlayServRecordQuery<InventoryItem>().Where(x => x.Durability < 25),
    maxRecords: 5_000);

PlayServBulkResult<PlayServRecord<InventoryItem>> saved =
    await items.BulkSaveAsync(all.Records, maxConcurrency: 4);
```

`LoadManyAsync` and `PopulateManyAsync` preserve input order and expose typed
per-item failures without discarding successful results. Plain reads deduplicate
IDs into chunks of at most 200 and page through `records:query` with `id in [...]`.
Duplicate inputs still receive independent handles; missing/invisible IDs remain
typed not-found items. A chunk failure affects its items, not successful chunks.
`maxConcurrency` bounds in-flight requests. Calls specifying `Fields` or `Expand`
retain point loads for projection/hydration compatibility. Cancellation aborts
the whole call rather than returning a partial-success batch. `DeleteByIdAsync`
accepts an optional ETag. `BulkSaveAsync`, `BulkDeleteAsync`, and
`DeleteAllAsync` remains a bounded client-side fan-out. For a single atomic
backend transaction (up to the backend limit), use `DeleteMatchingAsync` with
an explicit `MatchingRecords` or `AllRecords` confirmation. `BulkCreateAsync`
likewise uses the native all-or-nothing `records:bulk-create` endpoint and
returns server-minted IDs in request order.
Before deleting, `DeleteAllAsync` loads the complete matching set and aborts
without a delete request if `maxRecords` is exceeded. A filtered query requires
`MatchingRecords`; an unrestricted query requires explicit `AllRecords`.

### Typed nested filters

`query.Where(x => x.Profile.Region == "eu")` and the selector/operator overload
both support inclusion-field paths up to eight segments. Each segment follows
`PlayServJsonName` / `PlayServField`; ignored members are rejected. The same path
works in REST and realtime filters, including realtime OR groups. Each plane
keeps its existing operator restrictions (for example, `in` is REST-only). String paths
remain available as `Where("profile.region", PlayServQueryOperator.Eq, "eu")`.
The backend validates inclusion schema traversal: these are not relation joins.
For many-inclusions, each predicate is existential; two predicates need not
match the same array element. Sorting and natural-key selectors remain top-level.

### Native natural-key writes

`UpsertByNaturalKeyAsync(key, record, mode, ifMatch, idempotencyKey)` uses one
native request and returns `Id`/`Created`, without a follow-up read. `record`
may be a typed DTO, anonymous object or dictionary with wire field names.
Choose `PlayServUpsertMode.Seed` to leave an existing row unchanged, or `Managed`
to update the supplied fields. Managed upsert still uses backend create
validation; omitted required fields may be invalid. The backend checks natural
key uniqueness and rejects mismatched key values in the record payload.

`BulkUpsertAsync(rows, mode, defaults, idempotencyKey)` accepts 1–200
`PlayServBulkUpsertRow<T>` values and returns ordered `Rows`, `Created`, `Updated`.
It is one atomic transaction: a rejected row rejects the entire request.
Row values override defaults. Neither API automatically retries an ambiguous
network failure; retain the same idempotency key and payload for a retry.

`PatchByNaturalKeyAsync(key, patch, ifMatch)` returns the updated record and ETag;
`DeleteByNaturalKeyAsync(key, ifMatch)` waits for deletion. An optional ETag
protects against stale writes. These APIs also work through Game Server
`Records<T>()` and `AsPlayer(jwt).Records<T>()`; reads remain server-authorized.
`LoadOrCreateAsync` retains its existing lazy factory and read/recovery behavior.

Singleton entities use `GetSingletonAsync<T>()` or
`Records<T>().GetSingletonAsync()`. Their handles provide the same snapshot,
ETag, `HasPendingChanges`, `SaveAsync`, and `ReloadAsync` behavior.

## Public project health

`await PlayServStatus.GetProjectAsync("my-project", environment: "prod")` reads
`GET /status/{projectSlug}?env=...` without client/server credentials or player
login. It exposes public function deployment/revision state, invocation counters,
probe results and latency percentiles/history. Omit the environment to use the
backend's unfiltered view. Null counters, probes or metrics mean unavailable
evidence, not zero activity or a successful probe. Failures use
`PlayServStatusException.UnifiedError`; a missing project remains a 404.
Current/history/federation APIs are unchanged.

## Player matchmaking

`PlayServMatchmaking.HostRoomAsync(request, options, ct)` adds explicit player-requested
hosting (PSV-2694) via `POST /rooms/{slug}:host`. `PlayServHostRoomRequest` contains
`FunctionSlug`, optional flat `Attributes` (2048 UTF-8 bytes maximum) and `Region`;
it deliberately has no `RoomName`. `PlayServRoomHostOptions.Timeout` defaults to
45 seconds across authentication and HTTP. Request values are snapshotted before
the first await; the result resumes on the caller's Unity context.

Success is a matched `PlayServMatchResult`: its reservation contains the minted
invite code, ticket, nullable connect metadata and immutable **server-approved**
attributes. Requested values are wishes, not authoritative room settings. The game
owns travel and must not issue a second Join for the hosting player's ticket.
Host performs no automatic retry, polling, launch or networking connection;
timeout/cancellation after sending can leave a room created on the server.

`PlayServMatchmakingException.RoomFailureCode` distinguishes `RoomQuotaExceeded`,
`RoomHostUnavailable`, `RoomRefused`, `RoomUnreachable`, `RoomHostCapacityExhausted`
and `RegionUnavailable`; `UnifiedError.SourceCode` retains unknown backend codes,
`UnifiedError.Message` carries the credential-filtered reason and `RetryAfter`
preserves the optional server delay. Backend-aggregated refusals are not rewritten.
See the README's **Host a room** example. Coverage uses fixtures, not live integration.

### Direct game-server connection (opt-in)

`PlayServMatchmaking.CreateGameConnection(options)` creates one independent
`PlayServGameConnection`. Subscribe to `MessageReceived` and `Closed` before
`ConnectAsync(reservation, ct)`; retain it during gameplay, then `Disconnect()` or
`Dispose()`. `SendTextAsync` sends game-owned text, not platform RPC frames. The
platform `/ws` remains connected, including when a direct WebGL socket closes.

Connection uses the current signed-in player and the existing C# game-server
`playerId` / optional `displayName` / `token` / `reservationToken` handshake.
`HandshakeSent` confirms only opening and sending, never server admission or
gameplay readiness. The game defines those messages. There is no automatic retry,
new Host/Join, ticket replay, or reconnect. Pass `connection.ConnectAsync` as the
connector callback to `JoinRoomAndConnectAsync`, or connect directly with the
Host result's reservation without a second Join.

Options are copied at creation: default total `Timeout` is 12 seconds, optional
`DisplayName` is game-selected, and `AllowInsecureWebSocket` defaults to false.
WSS is the deployment default; plaintext WS requires explicit development opt-in.
Use explicit ws/wss connect strings or matching host/port/transport metadata. No
UDP conversion, address discovery, query credentials or TLS provisioning occurs.
Managed token refresh cannot create an anonymous replacement player. Identity
changes fail the attempt; there is no public token getter or persisted connector
credential. Unknown reservation lifetime is not inferred from `ExpiresAt`; known
expiry is checked before opening and before the handshake. Timeout after sending
does not roll back server admission. The handshake is capped at 4096 UTF-8 bytes;
serialized text sends and received messages at 1 MiB. Call from Unity's context.
`Closed` carries a safe normalized error, or null for local disconnect. Create a
new connection instance for another attempt. Fixture coverage is not live server
admission, a TLS deployment, or an implemented Tanks gameplay protocol.

`PlayServMatchmaking` calls the existing player-authenticated runtime
matchmaking endpoint. Configure or establish a player session first; the SDK
uses the current `RuntimeTokenProvider` JWT and public `pk_*` client token.

```csharp
using Playserv.Matchmaking;
using Playserv.Wrapper;

PlayServJoinGameResult join = await PlayServMatchmaking.JoinGameAsync(
    new PlayServJoinGameRequest
    {
        FunctionSlug = "tank-room",
        Matchmaker = "ranked",
        Parameters = new
        {
            mode = "duo",
            skill = 1700,
            party_id = currentPartyId
        },
        WaitMs = 20_000
    },
    cancellationToken: destroyCancellationToken);

if (join.Status == PlayServJoinGameStatus.Matched)
{
    ConnectToRoom(
        join.Reservation.RoomName,
        join.Reservation.ReservationToken);
}
```

`Parameters` accepts a typed DTO, anonymous object, or string-keyed dictionary.
It must serialize to a JSON object. The SDK snapshots it before the first
request and sends the same value through the backend's `params` field on every
`searching` retry and `room_closed` re-entry. The backend can validate it
against the matchmaker's configured lobby-state schema. Unity never sends
`player_id`; the platform derives the player from the verified JWT.

`PlayServFindMatchRequest` exposes the complete player request: function slug,
matchmaker, parameters, `WaitMs`, and `SearchAgeMs`. `PlayServJoinGameRequest`
exposes the same stable placement inputs except search age, which the SDK owns
and updates from elapsed time. The positional Find/Join overloads remain
backward compatible.

The default Join performs 20-second placement waits and handles `searching`
responses inside one call. Its UnityWebRequest deadline is 25 seconds: the same
`20_000` value serialized as `wait_ms`, plus a fixed five-second network
margin. Custom waits follow the same rule, rounded up to a whole second. A
request without a server wait uses a 10-second deadline. Caller cancellation
aborts the request and remains an `OperationCanceledException`.

The optional `tryEnter` callback can validate or enter the returned room before
the join completes. Throw `PlayServRoomEntryRefusedException("room_closed")`
from that callback to restart placement; other refusal codes propagate.

Operational Find, Join, and Launch failures throw
`PlayServMatchmakingException`. Its `Operation`, `FunctionSlug`, and
`UnifiedError` normalize backend Problem Details, network/timeouts, and invalid
success payloads while retaining the exact backend `SourceCode`. Caller
cancellation remains `OperationCanceledException`; invalid arguments and
missing configuration remain standard exceptions. The existing
`PlayServRoomEntryRefusedException.ErrorCode` remains available and now also
has `UnifiedError`.

Request an orchestrated game-server deployment when the game explicitly needs
to start capacity:

```csharp
PlayServServerLaunchResult launch =
    await PlayServMatchmaking.LaunchServerAsync(
        "tank-room",
        region: "eu-west",
        cancellationToken);

UnityEngine.Debug.Log($"Accepted deployment: {launch.DeploymentId}");
```

The `202` response means only that the orchestrator accepted the deployment.
It does not create a matchmaking room; the launched server must boot and
self-register before placement can find it.

Unity exposes only player-safe matchmaking calls. `ListRooms`, `UpsertRoom`,
`CloseRoom`, and `ConsumeReservation` require an `sk_*` server credential and
therefore remain in the PlayServ C# server SDK rather than the Unity player
runtime.

## Cloud functions

`PlayServCode` invokes deployed functions through the existing runtime
`/fn/{slug}` gateway. It is an HTTP surface and is separate from the live
WebSocket-based `PlayServRpc` API.

Typed POST calls serialize the request and deserialize the response:

```csharp
using System;
using Playserv.Code;
using Playserv.Wrapper;

[Serializable]
public sealed class GrantRewardResponse
{
    public string reward_id;
    public int amount;
}

PlayServFunctionResult<GrantRewardResponse> result =
    await PlayServCode.CallAsync<GrantRewardResponse>(
        "grant-daily-reward",
        new { streak = 7 },
        new PlayServFunctionCallOptions
        {
            Version = "v2",
            TimeoutSeconds = 30
        },
        cancellationToken);

if (!result.IsSuccess)
    UnityEngine.Debug.LogError(result.Error);
```

Set `PlayServFunctionCallOptions.StrictResponseTypes = true` to reject type
coercion, including string-to-number conversion, fractional/out-of-range integer
values, and invalid nested DTO/collection/dictionary values. The same option works
on `PlayServGameServer.Code.CallAsync`. Strict calls require a JSON response:
`CallAsync<string>` expects a JSON string (including quotes), and an empty body is
a deserialization failure. Default `false` preserves plain-text and empty-body compatibility.

The Newtonsoft codec implements optional `IPlayServStrictJsonCodec`. Unsupported
custom codecs, converters, or response contracts fail before I/O. Validation uses
the normal resolver's wire names, ignored and required fields; optional missing
and unknown fields remain compatible. Integer contracts require integer JSON
tokens. Default enums use numbers; `StringEnumConverter` uses strings and honors
its `AllowIntegerValues` setting. Guid/date/time/base64 values require string wire
forms. Mismatches return `Deserialization` with a field path and expected/actual
types, never field values. The explicitly exposed raw response remains available
to the caller and should not be logged indiscriminately. Raw/binary calls are unchanged.

Use the raw request when the function needs another HTTP verb, query values, a
non-JSON body, or selected pass-through headers:

```csharp
using System.Collections.Generic;

var response = await PlayServCode.InvokeAsync(
    new PlayServFunctionRequest
    {
        Slug = "profile",
        Method = PlayServFunctionMethod.Put,
        Query = new Dictionary<string, string> { ["region"] = "eu" },
        RawBody = "name=Alex",
        ContentType = "application/x-www-form-urlencoded",
        Headers = new Dictionary<string, string>
        {
            ["X-Correlation-Id"] = correlationId
        }
    },
    cancellationToken);
```

The gateway accepts GET, POST, PUT, PATCH and DELETE. `Version` is encoded as
`X-Playserv-Function-Version`; callers cannot inject Authorization,
`X-Playserv-Client`, or arbitrary `X-Playserv-*` values. The SDK supplies the
public `pk_*` credential and includes the current player JWT when one is
available. The gateway verifies that credential, strips it before forwarding,
and provides verified caller context to the function.

HTTP and Problem Details failures are returned as `PlayServError`. Invalid API
arguments and caller cancellation remain exceptions. A typed response that is
not valid JSON returns `PlayServErrorCode.Deserialization` while preserving the
raw response body and metadata.

Functions can also receive and return arbitrary bytes without UTF-8 conversion:

```csharp
var progress = new Progress<PlayServFunctionTransferProgress>(value =>
    UnityEngine.Debug.Log($"{value.Direction}: {value.BytesTransferred}"));

PlayServFunctionResult binary = await PlayServCode.InvokeBytesAsync(
    new PlayServFunctionRequest
    {
        Slug = "build-generated-asset",
        Method = PlayServFunctionMethod.Post,
        RawBodyBytes = sourceArchive,
        ContentType = "application/zip"
    },
    new PlayServFunctionTransferOptions
    {
        MaxResponseBytes = 16L * 1024L * 1024L,
        Progress = progress
    },
    cancellationToken);

if (binary.IsSuccess)
    ConsumeAsset(binary.Response.BodyBytes);
```

`Body`, `RawBody`, and `RawBodyBytes` are mutually exclusive. `BodyBytes` is the
authoritative buffered response for binary content; `Body` remains available
for text and JSON compatibility. The standard HTTP module implements the
additive `IPlayServRuntimeBinaryHttpClient` capability. A custom HTTP module
that has not opted into that interface returns `binary_http_not_supported`
without making a function request.

Large responses should be streamed to disk:

```csharp
PlayServFunctionDownloadResult download = await PlayServCode.DownloadToFileAsync(
    new PlayServFunctionRequest
    {
        Slug = "export-world",
        Method = PlayServFunctionMethod.Get
    },
    System.IO.Path.Combine(Application.persistentDataPath, "world.zip"),
    new PlayServFunctionDownloadOptions
    {
        MaxResponseBytes = 512L * 1024L * 1024L,
        OverwriteExistingFile = true,
        Progress = progress
    },
    cancellationToken);
```

The response is written to a unique temporary file beside the target and moved
only after a complete successful response. Cancellation or HTTP, network, size,
or persistence failure removes the partial file and preserves an existing
target. Buffered transfers default to 16 MiB; file transfers default to 512 MiB.
Exceeding the configured limit produces `PlayServErrorCode.InvalidResponse`
with `SourceCode == "function_response_too_large"`. File downloads require an
absolute path and are unavailable on WebGL; use `InvokeBytesAsync` there.

## Catalog and storefronts

The read-only commerce facades consume the runtime `GET /catalog/items`,
`GET /catalog/items/{itemId}`, `GET /storefronts`, and
`GET /storefronts/{id}` endpoints. They always send the configured public
`pk_*` token and also send the current managed or custom player JWT when one is
available.

```csharp
using Playserv.Commerce;
using Playserv.Wrapper;

PlayServStorefrontPage storefronts = await PlayServStorefronts.ListAsync(
    new PlayServStorefrontQuery
    {
        Status = "live",
        Audience = "all_players",
        Limit = 25
    },
    cancellationToken);

foreach (PlayServStorefront storefront in storefronts.Storefronts)
{
    foreach (PlayServStorefrontItem placement in storefront.Items)
    {
        PlayServCatalogItem item = await PlayServCatalog.GetAsync(
            placement.ItemId,
            cancellationToken);
        ShowStoreItem(item.Name, item.ImageUrl);
    }
}
```

Catalog list queries support `Status`, `Search`, `Sort`, `Cursor`, and `Limit`.
Storefront queries additionally support `Audience`. Page responses retain
`cursor_next`, `cursor_prev`, `has_more`, and `total_estimate`; page sizes must
be between 1 and 200. Full DTOs expose catalog localizations, bundle contents,
platform mappings, storefront audiences, schedules, and 30-day statistics.

HTTP Problem Details become `PlayServCommerceException.UnifiedError`, while
caller cancellation and invalid arguments remain exceptions. The facade does
not fabricate purchase or receipt-validation calls: the currently shipped
runtime commerce endpoints are read-only.

## Public platform status

`PlayServStatus` reads the credential-exempt `GET /status`,
`GET /status/history`, and `GET /status/federation` endpoints. Only the backend
address must be configured; these requests deliberately send neither
`X-PlayServ-Client` nor player authorization.

```csharp
using Playserv.Status;
using Playserv.Wrapper;

PlayServPlatformStatus current = await PlayServStatus.GetCurrentAsync(
    cancellationToken);

PlayServPlatformStatusHistory history = await PlayServStatus.GetHistoryAsync(
    pop: "iad-1",
    days: 30,
    cancellationToken: cancellationToken);

PlayServStatusFederation federation = await PlayServStatus.GetFederationAsync(
    cancellationToken);
```

Current status exposes the backend's open-ended system names and tri-state
health strings, probe age, and timestamps. History exposes daily availability
and green/yellow/red minute totals for a 1–365 day window. Federation origins
are validated as absolute HTTP(S) URIs and deduplicated, but are not fetched by
the SDK. This avoids silently trusting or contacting a different origin; a
game-owned status UI can apply its own allowlist first.

HTTP, network, and malformed-response failures throw
`PlayServStatusException` with a `UnifiedError`. Invalid arguments and caller
cancellation keep the normal .NET exception semantics.

## Apple Sign In module

Apple Sign In is an optional client module for iOS builds. It uses Apple's native
`AuthenticationServices.framework` and does not require any third-party auth SDK.

Install `com.playserv.apple-signin` before enabling the module:

```json
"com.playserv.apple-signin": "git@github.com:playserv1/playserv-unity-sdk.git?path=/CompanionPackages~/com.playserv.apple-signin#<tag-or-commit>"
```

### 1. Configure Apple Developer

1. Open the Apple Developer portal.
2. Create or select the App ID that matches the Unity iOS Bundle Identifier.
3. Enable the `Sign in with Apple` capability for that App ID.
4. If the game/backend uses Apple's web or REST auth flow, also create a Services ID, register the return URL, and create a Sign in with Apple private key.
5. Store these values only in backend secret storage:
   - Team ID
   - App Bundle ID or Services ID, depending on the flow your backend validates
   - Key ID
   - Redirect URI, if a Services ID/web flow is used
   - Sign in with Apple `.p8` private key

The PlayServ backend validates the Apple ID token through
`/auth/players/login`. Never place the `.p8` private key or a generated Apple
client secret in a Unity asset, game build, source repository, or client-side
environment variable.

Apple setup reference: https://developer.apple.com/documentation/signinwithapple/configuring-your-environment-for-sign-in-with-apple

### 2. Enable the PlayServ module

1. In Unity, open `Tools/PlayServ/Settings`.
2. Open `SDK module settings`.
3. Keep `Client Execution` enabled.
4. Enable `Apple Sign In`.
5. Go back to the main PlayServ window.
6. In the `Apple Sign In` section, click `Create/Select Settings`.

This creates `Assets/Resources/PlayServAppleSignInSettings.asset` in the game
project. The asset is project-side on purpose, so Package Manager installs do not
write credentials into the SDK package folder.

### 3. Fill Apple settings

Open `PlayServAppleSignInSettings.asset` and configure:

- `Request Email`: ask Apple for the user's email on first consent.
- `Request Full Name`: ask Apple for the user's name on first consent.
- `Default Nonce`: optional nonce sent with sign-in requests.
- `Default State`: optional state value returned with sign-in responses.
- `Client Id`: expected Apple audience for backend validation, usually the app Bundle ID for native iOS flows or the Services ID for web/service flows.
- `Add Sign In Capability On Build`: keep enabled unless you add the Xcode capability manually.
- `Entitlements File Name`: generated entitlements file name for the Xcode project.

Team ID, Services ID, Key ID, redirect URI, and private keys are intentionally
not part of `PlayServAppleSignInSettings`. They belong only to PlayServ backend
configuration. When upgrading from an older SDK, the editor
re-serializes existing Apple settings assets to remove those legacy fields.

Apple only returns `Email` and `FullName` the first time a user grants consent.
Do not treat those fields or `UserId` as a trusted PlayServ identity before
backend verification.

### 4. Build for iOS

1. Switch Unity build target to iOS.
2. Make sure the iOS Bundle Identifier matches the Apple App ID.
3. Build the Xcode project.
4. If `Add Sign In Capability On Build` is enabled, PlayServ adds:
   - `AuthenticationServices.framework`
   - Sign in with Apple capability
   - the configured entitlements file
5. In Xcode, confirm the target has the Sign in with Apple capability before archiving.

`PlayServAppleSignIn.IsAvailable` is expected to be `false` in the Unity Editor,
on Android, and on unsupported iOS versions.

### 5. Use Apple login at runtime

```csharp
using System;
using System.Threading.Tasks;
using Playserv.Wrapper;

public static class AppleLoginExample
{
    public static async Task Login()
    {
        if (!PlayServAppleSignIn.IsAvailable)
            return;

        var credential = await PlayServAppleSignIn.SignInAsync();

        var appleUserId = credential.UserId;
        var identityToken = credential.IdentityToken;
        var authorizationCode = credential.AuthorizationCode;

        if (!credential.TryCreateBackendProof(out var proof))
            throw new InvalidOperationException("Apple did not return an ID token.");

        var result = await PlayServAuth.LoginExternalAsync(proof);
        // Use PlayServAuth.LinkIdentityAsync(proof) when adding Apple to an
        // already registered managed player.
        if (!result.IsSuccess)
            Console.WriteLine(result.Error?.ToString() ?? "Apple account conflict.");
    }

    public static async Task CheckCredentialState(string appleUserId)
    {
        var state = await PlayServAppleSignIn.GetCredentialStateAsync(appleUserId);
        Console.WriteLine(state.State);
    }
}
```

### Apple troubleshooting

- `IsAvailable` is `false`: run on an iOS device/build with the module enabled.
- Xcode capability is missing: keep `Add Sign In Capability On Build` enabled or add the capability manually in Xcode.
- `Email` or `FullName` is empty: Apple returns these only on first consent.
- Backend proof is unavailable: Apple returned an authorization code but no ID token; the helper never submits an authorization code as an ID token.
- Backend token validation fails: check Bundle ID/Services ID, Team ID, Key ID, nonce, and the expected audience value.

## Google Sign In module

Google Sign In is an optional client module for Android and iOS builds. PlayServ
provides the module toggle, settings asset, and runtime facade. The game project
must also contain a Google Sign-In Unity provider plugin so native Android/iOS
sign-in can run.

Install `com.playserv.google-signin` before enabling the module:

```json
"com.playserv.google-signin": "git@github.com:playserv1/playserv-unity-sdk.git?path=/CompanionPackages~/com.playserv.google-signin#<tag-or-commit>"
```

Google Unity plugin reference: https://github.com/googlesamples/google-signin-unity

### 1. Configure Google Cloud credentials

1. Open Google Cloud Console.
2. Configure the OAuth consent screen for the game.
3. Create an OAuth 2.0 `Web application` client.
4. Copy the Web client ID. This is the value used by PlayServ `Web Client Id`.
5. For Android builds, create an Android OAuth client with:
   - the Unity Android package name
   - the SHA-1 fingerprint of the same keystore used to sign the build
6. For iOS builds, create an iOS OAuth client with:
   - the Unity iOS Bundle Identifier
   - the URL scheme / plist setup required by the installed Google Sign-In Unity plugin

The Web client ID is required when requesting an ID token or a server auth code.

### 2. Install the Google Sign-In provider plugin

1. Import the Google Sign-In Unity plugin into the game project.
2. Run the Android/iOS dependency resolver required by that plugin.
3. For Android, confirm Unity `Player Settings > Android > Package Name` matches the Android OAuth client.
4. For Android release builds, confirm the signing keystore matches the SHA-1 fingerprint registered in Google Cloud.
5. For iOS, follow the plugin's iOS setup and ensure the generated Xcode project contains the required Google configuration.

PlayServ does not compile against Google classes directly. If the Google plugin is
not installed, the SDK still compiles, but `PlayServGoogleSignIn.IsAvailable`
returns `false`.

### 3. Enable the PlayServ module

1. In Unity, open `Tools/PlayServ/Settings`.
2. Open `SDK module settings`.
3. Keep `Client Execution` enabled.
4. Enable `Google Sign In`.
5. Go back to the main PlayServ window.
6. In the `Google Sign In` section, click `Create/Select Settings`.

This creates `Assets/Resources/PlayServGoogleSignInSettings.asset` in the game
project. The asset stays outside the SDK package folder, so Package Manager
installs can be updated without overwriting game credentials.

### 4. Fill Google settings

Open `PlayServGoogleSignInSettings.asset` and configure:

- `Web Client Id`: OAuth 2.0 Web application client ID from Google Cloud.
- `Request Id Token`: enable when the backend needs a Google ID token.
- `Request Auth Code`: enable when the backend exchanges a server auth code.
- `Request Email`: include the user's email in the returned profile.
- `Force Token Refresh`: request a fresh server auth code when supported by the provider plugin.
- `Use Game Sign In`: enable only when using the Google Play Games profile flow supported by the provider plugin.
- `Hosted Domain`: optional Google Workspace hosted-domain hint.
- `Account Name`: optional preferred account hint.
- `Additional Scopes`: optional extra Google OAuth scopes required by the game.

For PlayServ authentication, keep `Request Id Token` enabled and configure the
Web client ID. `Request Auth Code` is optional and is only needed for a separate
application-owned server exchange; PlayServ never submits an auth code as an ID
token.

### 5. Use Google login at runtime

```csharp
using System.Threading.Tasks;
using Playserv.Wrapper;

public static class GoogleLoginExample
{
    public static async Task Login()
    {
        if (!PlayServGoogleSignIn.IsAvailable)
            return;

        var credential = await PlayServGoogleSignIn.SignInAsync();
        var idToken = credential.IdToken;
        var authCode = credential.AuthCode;
        var googleUserId = credential.UserId;

        if (credential.TryCreateBackendProof(out var proof))
        {
            var result = await PlayServAuth.LoginExternalAsync(proof);
            // Use PlayServAuth.LinkIdentityAsync(proof) to add Google as an
            // additional provider for the current managed player.
            if (!result.IsSuccess)
                UnityEngine.Debug.LogError(
                    result.Error?.ToString() ?? "Google account conflict.");
        }
    }
}
```

Optional sign-out helpers:

```csharp
PlayServGoogleSignIn.SignOut();
PlayServGoogleSignIn.Disconnect();
```

### Google troubleshooting

- `IsAvailable` is `false`: the Google Sign-In Unity provider plugin is missing, not loaded, or the PlayServ module is disabled.
- ID token is empty: enable `Request Id Token` and set `Web Client Id`.
- Auth code is empty: enable `Request Auth Code` and set `Web Client Id`.
- Backend proof is unavailable: Google returned an auth code but no ID token; the helper never submits an auth code as an ID token.
- Android login fails: verify package name, SHA-1 fingerprint, keystore, and resolver output.
- iOS login fails: verify Bundle Identifier, URL scheme/plist setup, and Xcode project configuration.

`PlayServExternalIdentityProof` contains only the provider ID, provider token,
optional provider mode, and nonce. It does not contain email or provider user
ID because client profile values must not be accepted as proof of identity. The
proof remains unverified until `PlayServAuth.LoginExternalAsync` sends it to the
PlayServ backend for signature, audience, issuer, expiry, and nonce validation.

## Steam Auth module

Install `com.playserv.steam-auth` alongside a game-owned Steamworks.NET
installation. The PlayServ package has no direct Steamworks.NET dependency and
finds the supported API through reflection:

```json
"com.playserv.steam-auth": "git@github.com:playserv1/playserv-unity-sdk.git?path=/CompanionPackages~/com.playserv.steam-auth#<tag-or-commit>"
```

The game remains responsible for `SteamAPI.Init`, `SteamAPI.Shutdown`, and
regular `SteamAPI.RunCallbacks()` pumping. Enable `Steam Auth` in SDK module
settings, then use the one-step helper:

```csharp
using Playserv.Wrapper;

if (PlayServSteamAuth.IsAvailable && PlayServSteamAuth.IsInitialized)
{
    PlayServAuthResult result = await PlayServSteamAuth.LoginAsync(
        PlayServExternalLoginMode.PreserveCurrentPlayer);

    // Add Steam to the current managed player instead:
    // result = await PlayServSteamAuth.LinkAsync();
}
```

The helper obtains `GetAuthTicketForWebApi(null)`, correlates the asynchronous
callback by its ticket handle, submits only `m_cubTicket` bytes as lowercase
hex, and cancels the ticket after the PlayServ request completes. The `null`
identity matches current PlayServ backend ticket validation. For manual use,
`GetCredentialAsync` returns an `IDisposable` credential whose
`TryCreateBackendProof` method builds the Steam proof.

`IsAvailable` means a compatible Steamworks.NET API was found;
`IsInitialized` additionally requires Steam to be running and the local user to
be logged on. Credential acquisition failures throw
`PlayServSteamAuthException`. The facade never logs or persists ticket bytes.

## Epic Auth module

Install `com.playserv.epic-auth` alongside the game-owned EOS-Contrib/PlayEveryWare
plugin and enable `Epic Auth` in SDK module settings:

```json
"com.playserv.epic-auth": "git@github.com:playserv1/playserv-unity-sdk.git?path=/CompanionPackages~/com.playserv.epic-auth#<tag-or-commit>"
```

EOS initialization and EAS login remain game responsibilities. To copy the
current local Epic Account Services access token:

```csharp
using Playserv.EpicAuth;
using Playserv.Wrapper;

PlayServAuthResult result = await PlayServEpicAuth.LoginAsync(
    PlayServEpicAuthRequest.Eos());

// Pass an Epic Account ID explicitly when selecting another local EAS user:
// result = await PlayServEpicAuth.LinkAsync(
//     PlayServEpicAuthRequest.Eos(epicAccountId));
```

An EOS Product User ID belongs to the Connect identity path and is not an Epic
Account ID. Do not pass a Product User ID to `Eos(epicAccountId)`.

For an Epic Games Launcher start, the package can read the exchange code from
`GetCommandLineArgsFromEpicLauncher().authPassword`:

```csharp
PlayServAuthResult result = await PlayServEpicAuth.LoginAsync(
    PlayServEpicAuthRequest.Launcher());
```

`Launcher(exchangeCode)` accepts an explicit code. The credential selects the
backend `launcher_exchange_code` mode automatically. A missing or incompatible
EOS plugin makes `IsAvailable` false; provider failures throw
`PlayServEpicAuthException` before PlayServ HTTP begins. Tokens are never logged
or persisted.

## Facebook Limited Login module

Install `com.playserv.facebook-login` alongside Meta Unity SDK and enable
`Facebook Login` in SDK module settings:

```json
"com.playserv.facebook-login": "git@github.com:playserv1/playserv-unity-sdk.git?path=/CompanionPackages~/com.playserv.facebook-login#<tag-or-commit>"
```

The game must complete `FB.Init` first. The adapter deliberately supports only
Meta Limited Login:

```csharp
using Playserv.FacebookLogin;
using Playserv.Wrapper;

var request = new PlayServFacebookLoginRequest(
    new[] { "public_profile", "email" });

PlayServAuthResult result = await PlayServFacebookLogin.LoginAsync(
    request,
    PlayServExternalLoginMode.PreserveCurrentPlayer);

// Add Facebook to the current managed player instead:
// result = await PlayServFacebookLogin.LinkAsync(request);
```

The bridge invokes
`FB.Mobile.LoginWithTrackingPreference(LoginTracking.LIMITED, permissions, nonce, callback)`.
When no nonce is supplied it generates a cryptographically secure 32-byte
base64url value. Missing, canceled, errored, or nonce-mismatched callbacks fail
with `PlayServFacebookLoginException` before any PlayServ HTTP request. Only
`CurrentAuthenticationToken().TokenString` and its matching nonce become the
backend proof; regular Facebook access tokens are not used. Tokens and nonces
are never logged or persisted.

For all three companion facades, automatic Android/iOS fingerprint collection
continues through the single downstream `PlayServAuth.LoginExternalAsync` call.
Provider acquisition errors are exceptions, while backend authentication and
identity conflicts remain ordinary `PlayServAuthResult` failures.

## 1) Configure SDK

### Option A: through Unity asset (recommended for editor workflow)

1. Open `Tools/PlayServ/Settings`.
2. In `PlayServ Config`, ensure `Assets/Resources/PlayServConfig.asset` exists.
3. Fill `GameVersion`, the public `ClientToken` (`pk_*`), and the backend endpoint.
4. On runtime start, call `PlayServ.Connect()`.

The public token identifies the project. The SDK obtains the player identity
from the managed auth session or the JWT supplied by a runtime token provider;
neither a game ID nor a caller-supplied user ID is sent in the WebSocket
handshake. `DeploymentGameId` is optional and is used only by Editor deployment
and latest-version tooling.

### Option B: configure from code

```csharp
using Playserv.Wrapper;

PlayServ.Config(new PlayServSettings
{
    ClientToken = "pk_...",
    GameVersion = "1.0.0",
    SdkVersion = PlayServ.SdkVersion,
    BackendServerAddress = "wss://playserv-proxy.test.playserv.io/ws",
    AllowMultipleConnections = true,
    KeepAlivePingIntervalMs = 30000,
    KeepAlivePongTimeoutMs = 10000
});
```

### Runtime player JWT

Player JWTs are never stored in `PlayServConfig`. Register a runtime provider
before connecting:

```csharp
using Playserv.Runtime.Abstractions;
using Playserv.Wrapper;

PlayServ.SetRuntimeTokenProvider(
    new PlayServDelegateRuntimeTokenProvider(async cancellationToken =>
    {
        return await sessionService.GetPlayServJwtAsync(cancellationToken);
    }));

await PlayServ.Connect();
```

The provider may return a raw JWT or `Bearer <jwt>`. It is queried before the
initial handshake and automatic reconnects. Runtime code rejects `sk_*` keys,
and `ClientToken` accepts only public `pk_*` values.

For migrations from the legacy connection API, remove `GameId` and `UserId`
from `PlayServSettings` and from positional `Config` calls. Use
`PlayServAuth.PlayerId` for the verified current player. Move a deployment-only
identifier to `DeploymentGameId` only when latest-version or Editor deployment
tools require it.

### Deployment credential

Deployment credentials are Editor-only and are not serialized into
`PlayServConfig`:

- CI: set `PLAYSERV_DEPLOY_AUTH_TOKEN`.
- Local development: enter the token in the `Deployment` section of the
  PlayServ window and click `Save`.
- The environment variable has priority over local Editor storage.

When an older config is opened, the SDK moves its legacy `deployAuthToken` to
project-scoped local Editor storage and removes serialized `authorization`.
Rotate any `sk_*` key that was previously committed to Git.

## 2) Connection lifecycle (MonoBehaviour)

```csharp
using Playserv.Proxy.Common;
using Playserv.Wrapper;
using UnityEngine;

public sealed class PlayServBootstrap : MonoBehaviour
{
    private async void Start()
    {
        PlayServ.OnTransportError += OnTransportError;
        PlayServ.OnKeepAlivePingSent += OnPing;
        PlayServ.OnKeepAlivePongReceived += OnPong;

        // If you already have Resources/PlayServConfig.asset, Config(...) is optional.
        PlayServ.Config(new PlayServSettings
        {
            ClientToken = "pk_...",
            GameVersion = "1.0.0"
        });

        bool connected = await PlayServ.Connect();
        if (!connected)
        {
            Debug.LogError("PlayServ connection failed.");
            return;
        }

        Debug.Log($"PlayServ connected. State={PlayServ.State}, SDK={PlayServ.SdkVersion}");
    }

    private void OnDestroy()
    {
        PlayServ.OnTransportError -= OnTransportError;
        PlayServ.OnKeepAlivePingSent -= OnPing;
        PlayServ.OnKeepAlivePongReceived -= OnPong;
        PlayServ.Disconnect();
    }

    private static void OnTransportError(TransportError error)
    {
        Debug.LogError($"PlayServ transport error: {error}");
    }

    private static void OnPing()
    {
        Debug.Log("PlayServ keepalive ping sent.");
    }

    private static void OnPong()
    {
        Debug.Log("PlayServ keepalive pong received.");
    }
}
```

## 3) Send commands

```csharp
using System;
using Playserv.Wrapper;

[Serializable]
public sealed class JoinMatchCommand
{
    public string MatchId;
}

[Serializable]
public sealed class PingCommand
{
    public long ClientTimeUnixMs;
}

public static class CommandExamples
{
    public static void SendExamples()
    {
        // Sends with explicit backend module name.
        PlayServRpc.Send(
            new JoinMatchCommand { MatchId = "match-001" },
            moduleName: "module_matchmaking");

        // Sends without module prefix.
        PlayServRpc.Send(
            new PingCommand { ClientTimeUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
    }
}
```

## 4) Subscribe to events

Group subscription failures preserve the backend message and numeric code in
`PlayServGroupSubscriptionException.UnifiedError`. Code `02006` maps to
`group_subscription_limit_reached` (Transport, non-retryable). It can mean the
configured subscription-count limit or group-key-length limit was exceeded.
The SDK does not impose a fixed group count, automatically retry the refusal,
or replay a rejected group after reconnect. Adjust subscriptions/the group key
before retrying. This transport subscription is not the GroupLeave/roster API.

Event subscriptions are keyed by their event type. Multiple observers of the
same type share one connection subscription, and disposing the final observer
removes it locally. Active event types are subscribed again automatically after
a reconnect. If the backend rejects a topic, that observable receives a
`PlayServEventSubscriptionException` with a normalized `UnifiedError`; other
event types continue running.

The current backend protocol does not accept client-originated
`EventMessage`, `GroupEventMessage`, or `UserEventMessage` commands. Therefore
`PlayServEvents.Publish*` throws `PlayServEventPublishingException` before
transport I/O. Use RPC, Code, or Records for client-to-server mutations.

```csharp
using System;
using Playserv.Events;
using Playserv.Wrapper;
using UnityEngine;

[Serializable]
[Event(EventType.All)]
public sealed class ChatMessageEvent
{
    public string FromUserId;
    public string Text;
}

public sealed class ChatEventsExample : MonoBehaviour
{
    private IDisposable _chatSubscription;

    private void OnEnable()
    {
        _chatSubscription = PlayServEvents.Subscribe<ChatMessageEvent>(OnChatMessage);
    }

    private void OnDisable()
    {
        _chatSubscription?.Dispose();
        _chatSubscription = null;
    }

    private static void OnChatMessage(ChatMessageEvent evt)
    {
        Debug.Log($"[CHAT] {evt.FromUserId}: {evt.Text}");
    }
}
```

## 5) Shared entity (data subscription) with `SelectEntity`

```csharp
using System;
using System.Threading.Tasks;
using Playserv.DataSubscription;
using Playserv.DataSubscription.Exceptions;
using Playserv.Wrapper;
using UnityEngine;

[Serializable]
public sealed class PlayerEntity
{
    public string Id;
    public string Nickname;
    public int Score;
    public int Hp;
}

public sealed class PlayerHudDto
{
    public string Nickname { get; set; } = "";
    public int Score { get; set; }
    public int Hp { get; set; }
}

public sealed class PlayerHudSync : MonoBehaviour
{
    private ISharedEntity<PlayerHudDto> _shared;

    public async Task BindAsync(string playerId)
    {
        _shared = await PlayServData.SelectEntity<PlayerEntity, PlayerHudDto>(
            playerId,
            map: src => new PlayerHudDto
            {
                Nickname = src.Nickname,
                Score = src.Score,
                Hp = src.Hp
            });

        _shared.Changed += OnChanged;
        _shared.Error += OnError;
        _shared.Failure += error => Debug.LogError(error.ToString());
        _shared.Terminated += OnTerminated;
    }

    public async Task DealDamageAsync(int damage)
    {
        if (_shared == null)
            return;

        await _shared.UpdateAsync(dto => dto.Hp = Mathf.Max(0, dto.Hp - damage));
    }

    public async Task ForceRefreshAsync()
    {
        if (_shared != null)
            await _shared.RefreshAsync();
    }

    private static void OnChanged(PlayerHudDto dto)
    {
        Debug.Log($"HUD updated: {dto.Nickname}, HP={dto.Hp}, Score={dto.Score}");
    }

    private static void OnError(DataSubscriptionException ex)
    {
        Debug.LogError($"Data subscription error [{ex.ErrorCode}]: {ex.Message}");
    }

    private static void OnTerminated()
    {
        Debug.LogWarning("Data subscription terminated by server.");
    }

    private void OnDestroy()
    {
        if (_shared != null)
        {
            _shared.Changed -= OnChanged;
            _shared.Error -= OnError;
            _shared.Terminated -= OnTerminated;
        }

        _shared?.Dispose(); // best-effort server close on the final local handle
        _shared = null;
    }
}
```

## 6) Spawn networked prefab

```csharp
using Playserv.Spawn;
using Playserv.Wrapper;
using UnityEngine;

public sealed class SpawnExample : MonoBehaviour
{
    public async void SpawnCrate()
    {
        // Path is relative to a Resources folder.
        // Example file path: Assets/Resources/NetworkPrefabs/Crate.prefab
        var go = await PlayServSpawn.Spawn("NetworkPrefabs/Crate", new Vector3(0f, 1f, 0f), Quaternion.identity);
        if (go == null)
        {
            Debug.LogError("Spawn failed. Check prefab path/components.");
            return;
        }

        var networkObject = go.GetComponent<NetworkObject>();
        Debug.Log($"Spawned network object: id={networkObject.NetworkId}, localOwner={networkObject.IsLocallyOwned}");
    }
}
```

Prefab requirements:
- Must be inside `Resources`.
- Must contain `NetworkObject`.
- Add `NetworkTransform` if you want transform replication.

## 7) Editor workflow (from package window)

Open `Tools/PlayServ/Settings` and use:
- `Code Generation` -> Generate/cleanup DTO files (`Assets/Shared/Generated/DTOs`).
- `Events` -> Generate event API (`Assets/Shared/Generated/Events`).
- `Model` -> Check schema updates and regenerate models (`Assets/Shared/Generated/Models`).
- `Deployment` -> ZIP and upload selected files to deployment API.

## 8) Practical notes

- `PlayServ.Connect()` throws if config is missing required fields.
- `PlayServEvents.Subscribe<T>()` is event subscription (module events), not a raw command response channel.
- Always dispose subscriptions and disconnect in object teardown.
- `PlayServSpawn.Spawn(...)` returns `null` when prefab path is invalid or missing `NetworkObject`.

## 9) Typed awaitable RPC

Use `PlayServRpc.InvokeAsync<TRequest, TResponse>` for business operations that
need a response. The SDK serializes the request, assigns a request ID, awaits
the matching response, and deserializes its JSON result.

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Playserv.RPC;
using Playserv.Wrapper;

public sealed class FindMatchRequest
{
    public string Mode { get; set; }
}

public sealed class FindMatchResponse
{
    public string MatchId { get; set; }
}

public static async Task<FindMatchResponse> FindMatchAsync(
    CancellationToken cancellationToken)
{
    PlayServRpcResult<FindMatchResponse> result =
        await PlayServRpc.InvokeAsync<FindMatchRequest, FindMatchResponse>(
            "RoomService",
            "FindMatch",
            new FindMatchRequest { Mode = "duo" },
            cancellationToken);

    if (!result.IsSuccess)
        throw new InvalidOperationException(result.Error.ToString());

    return result.Value;
}
```

`PlayServRpcResult<T>` exposes the generated `RequestId`, typed `Value`, raw JSON
result, server status/message, timestamp, and a structured `Error`. Timeout and
cancellation are returned as `PlayServRpcErrorCode.Timeout` and
`PlayServRpcErrorCode.Canceled`.

The default timeout is 30 seconds. `PlayServRpcInvokeOptions` can override the
timeout, request ID, and coalescing key. Set `StrictResponseTypes = true` on these
options for the same recursive JSON checks described under Cloud functions.
The setting is captured per invocation, so concurrent strict/compatible calls do
not interfere. Unsupported contracts/codecs fail before sending; invalid or empty
JSON responses return `DeserializationFailed` with payload-free diagnostics.
Request serialization, cancellation, timeout and response correlation are unchanged.
New gateways correlate by request ID;
older gateways fall back to oldest-first matching for the same service and
method.

You can pair this with event subscription for side effects from RPC handlers:

```csharp
using System;
using UnityEngine;
using Playserv.Wrapper;

[Serializable]
public sealed class NotificationEvent
{
    public string EventId;
    public string Message;
    public DateTime Timestamp;
    public string EventType;
}

public sealed class RpcNotificationListener : MonoBehaviour
{
    private IDisposable _subscription;

    private void OnEnable()
    {
        _subscription = PlayServEvents.Subscribe<NotificationEvent>(evt =>
        {
            Debug.Log($"[RPC Event] {evt.EventType}: {evt.Message}");
        });
    }

    private void OnDisable()
    {
        _subscription?.Dispose();
        _subscription = null;
    }
}
```

## 10) Low-level transport debug (advanced)

If you need protocol-level validation tests (like in `Assets/Tests/EntryPointConsoleCommands.cs`), you can bypass serializer and send raw JSON bytes:

```csharp
using System.Text;
using System.Threading.Tasks;
using Playserv.Proxy.Interfaces;
using Playserv.Wrapper;
using UnityEngine;

public static class TransportDebugExample
{
    public static async Task SendRawJsonAsync(string rawJson)
    {
        ITransportImplementation transport = PlayServ.GetTransportImplementation();
        if (transport == null)
        {
            Debug.LogError("Transport implementation is not available. Connect SDK first.");
            return;
        }

        await transport.Send(Encoding.UTF8.GetBytes(rawJson));
    }
}
```

## 11) Connection state UI (pattern from `Assets/Tests/PlayServStateUI.cs`)

Simple UI binding for connection state and connect/disconnect buttons:

```csharp
using Playserv.Proxy.Common;
using Playserv.Wrapper;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class PlayServStateUiExample : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI _stateText;
    [SerializeField] private Button _connectButton;
    [SerializeField] private Button _disconnectButton;

    private void Awake()
    {
        _connectButton.onClick.AddListener(OnConnectClick);
        _disconnectButton.onClick.AddListener(OnDisconnectClick);
    }

    private void Update()
    {
        PlayServState state = PlayServ.State;
        _stateText.text = $"PlayServ.State: {state}";
        _connectButton.interactable = state == PlayServState.Offline;
        _disconnectButton.interactable = state == PlayServState.Online;
    }

    private async void OnConnectClick()
    {
        bool ok = await PlayServ.Connect();
        Debug.Log($"Connect result: {ok}");
    }

    private static void OnDisconnectClick()
    {
        PlayServ.Disconnect();
    }
}
```

## 12) KeepAlive stats widget (pattern from `Assets/Tests/KeepAliveStatsUI.cs`)

```csharp
using System.Threading;
using Playserv.Wrapper;
using TMPro;
using UnityEngine;

public sealed class KeepAliveStatsExample : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI _statsText;
    private SynchronizationContext _mainThread;
    private int _pingSent;
    private int _pongReceived;

    private void Awake()
    {
        _mainThread = SynchronizationContext.Current;
    }

    private void OnEnable()
    {
        PlayServ.OnKeepAlivePingSent += OnPingSent;
        PlayServ.OnKeepAlivePongReceived += OnPongReceived;
    }

    private void OnDisable()
    {
        PlayServ.OnKeepAlivePingSent -= OnPingSent;
        PlayServ.OnKeepAlivePongReceived -= OnPongReceived;
    }

    private void OnPingSent()
    {
        _pingSent++;
        Redraw();
    }

    private void OnPongReceived()
    {
        _pongReceived++;
        Redraw();
    }

    private void Redraw()
    {
        _mainThread.Post(_ =>
        {
            _statsText.text = $"KeepAlive - Sent: {_pingSent} | Received: {_pongReceived}";
        }, null);
    }
}
```

## 13) Data subscription UI flow (pattern from `Assets/Tests/DataSubscriptionEntryPoint.cs`)

Working UI flow from tests:
- generate player id at startup
- bind via `SelectEntity<Player, PlayerDto>(playerId, map)`
- update UI in `Changed`
- handle `Error` and `Terminated`
- mutate via `Update/UpdateAsync`
- force overwrite sync via `RefreshAsync`

Minimal command-style operations:

```csharp
// Rename
_player.Update(p => p.Name = newName);

// Increment level
_player.Update(p => p.Level++);

// Set level async
await _player.UpdateAsync(p => p.Level = level);

// Request full overwrite from server
await _player.RefreshAsync();
```

## 14) `[Shared]` DTO generation from a ViewModel (pattern from `Assets/Tests/ViewModels/ViewModel.cs`)

```csharp
using Playserv.Shared;

public class ViewModel
{
    [Shared(typeof(Shared.Generated.Models.Player), "player", Selection = "{ Level }")]
    private ViewModel_PlayerLevel Level { get; set; }
}
```

Then run code generation from `Tools/PlayServ/Settings` -> `Code Generation` -> `Generate DTOs Now`.

## Package tests

The package contains Edit Mode tests in `Tests/Editor` and Play Mode-compatible
runtime tests in `Tests/Runtime`.

For a Git or registry package, opt the package into Unity Test Framework discovery
from the consuming project's `Packages/manifest.json`:

```json
{
  "testables": [
    "com.playserv.sdk"
  ]
}
```

Run the normal suites from `Window` -> `General` -> `Test Runner`, or in batch mode:

```bash
Unity \
  -batchmode \
  -projectPath /path/to/project \
  -runTests \
  -testPlatform EditMode \
  -testResults /path/to/editmode-results.xml \
  -quit

Unity \
  -batchmode \
  -projectPath /path/to/project \
  -runTests \
  -testPlatform PlayMode \
  -testResults /path/to/playmode-results.xml \
  -quit
```

The IL2CPP build smoke test is intentionally opt-in because it produces a full
player build and requires the active target's IL2CPP support module:

```bash
PLAYSERV_RUN_IL2CPP_TESTS=1 Unity \
  -batchmode \
  -projectPath /path/to/project \
  -runTests \
  -testPlatform EditMode \
  -testCategory IL2CPP \
  -testResults /path/to/il2cpp-results.xml \
  -quit
```
