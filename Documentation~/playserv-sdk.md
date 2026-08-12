# PlayServ Unity SDK

This document contains practical examples for the public runtime API exposed by:
- `Playserv.Wrapper.PlayServ`
- `Playserv.Wrapper.PlayServData`
- `Playserv.Wrapper.PlayServEvents`
- `Playserv.Wrapper.PlayServAnalytics`
- `Playserv.Wrapper.PlayServRpc`
- `Playserv.Wrapper.PlayServSpawn`
- `Playserv.Wrapper.PlayServServerRpc`
- `Playserv.Wrapper.PlayServSettings`
- `Playserv.DataSubscription.ISharedEntity<T>`
- `Playserv.Spawn` components

## Installation

### UnityPackage / Assets import

When the SDK is imported under `Assets/playserv-unity-sdk`, Unity does not read this package's `package.json` dependencies. The SDK attempts to install `com.unity.nuget.newtonsoft-json` automatically in that mode. If Package Manager cannot install it, add this dependency manually to the game project's `Packages/manifest.json`:

```json
"com.unity.nuget.newtonsoft-json": "3.2.2"
```

### OpenUPM

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
    "com.playserv.sdk": "0.3.6"
  }
}
```

## Versioning

- Package version is defined in `package.json` (`version`).
- Use Semantic Versioning: `MAJOR.MINOR.PATCH`.
- Release tag should match package version with `v` prefix (example: `v0.1.0`).
- `CHANGELOG.md` must include a heading for the same package version.
- The editor window displays the installed package version from Package Manager/package.json.
- Runtime SDK version constants are synchronized from `package.json` by `Tools/PlayServ/Internal/Sync SDK Version From package.json`.

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
Apple Sign In, Google Sign In, Analytics, Pulse, Native Transports, and WebRTC
through Unity Package Manager. Package state and module state are intentionally
separate:

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
    [PlayServSchema("player.profile", Version = "1")]
    [PlayServFormerlyNamed("LegacyPlayerProfile")]
    public sealed class PlayerProfile
    {
        [PlayServField("playerId", Required = PlayServRequiredMode.Required)]
        public string Id { get; set; }

        public int Level { get; set; }
        public List<string> Tags { get; set; }
        public DateTime? LastSeenAt { get; set; }

        [PlayServIgnore]
        public string LocalOnlyCache { get; set; }
    }
}
```

- `PlayServSchema` sets the stable schema ID, authority, and optional version.
- `PlayServField` overrides the serialized name or required state.
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
    "endpoint": "",
    "environment": "",
    "projectId": ""
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

### Schema Service boundary

Version 1 establishes the local canonical graph, deterministic files, source
authorities, and a versioned tool protocol. A `remote` source may be declared,
but `sync` currently performs local reconciliation only. Upload/download and
conflict resolution will be enabled after the Schema Service API defines
authentication, revisions, ownership, and breaking-change policy. Tokens will
be read from the process environment or OS credential storage, never from
`playserv.schema.json` or command-line arguments.

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
handler is available.

### Provider and backend contract

By default, the module sends the serializable `PlayServAnalyticsBatch` command
as `TrackAnalyticsBatch` to `module_analytics`. The PlayServ backend still needs
an ingestion handler that authenticates the session, validates and
deduplicates `EventId`, stores events, and exposes reporting or export.

Until that handler is deployed, route events to another destination by
implementing `IPlayServAnalyticsProvider`. The implementation belongs to the
client project, for example under `Assets/Analytics`; it is not shipped by the
PlayServ SDK:

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

Return to the built-in `module_analytics` transport when needed:

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

The PlayServ SDK does not currently provide the backend endpoint that exchanges
or validates Apple credentials. Never place the `.p8` private key or a generated
Apple client secret in a Unity asset, game build, source repository, or client-side
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
not part of `PlayServAppleSignInSettings`. They belong to the future PlayServ
authentication backend. When upgrading from an older SDK, the editor
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

        if (credential.TryCreateBackendProof(out var proof))
        {
            // proof is still unverified. Send it to the PlayServ auth backend
            // when that endpoint is available.
        }

        // Until backend verification exists, do not use appleUserId, email,
        // identityToken, or authorizationCode to create a trusted PlayServ session.
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
- Backend token validation fails after server auth is introduced: check Bundle ID/Services ID, Team ID, Key ID, nonce, and the expected audience value.

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

For the common PlayServ/backend flow, keep `Request Id Token` and
`Request Auth Code` enabled. The current SDK returns these provider credentials
but does not yet exchange them for a verified PlayServ session.

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
            // proof is still unverified. Send it to the PlayServ auth backend
            // when that endpoint is available.
        }

        // Until backend verification exists, do not use googleUserId, email,
        // idToken, or authCode to create a trusted PlayServ session.
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
- Android login fails: verify package name, SHA-1 fingerprint, keystore, and resolver output.
- iOS login fails: verify Bundle Identifier, URL scheme/plist setup, and Xcode project configuration.

`PlayServExternalIdentityProof` deliberately contains only the provider ID,
ID token, and authorization code. It does not contain email or provider user ID,
because client profile values must not be accepted as proof of identity. A proof
remains unverified until a future PlayServ backend endpoint validates its
signature, audience, issuer, expiry, and nonce where applicable.

## 1) Configure SDK

### Option A: through Unity asset (recommended for editor workflow)

1. Open `Tools/PlayServ/Settings`.
2. In `PlayServ Config`, ensure `Assets/Resources/PlayServConfig.asset` exists.
3. Fill `GameId`, `UserId`, `GameVersion`, and the public `ClientToken` (`pk_*`).
4. On runtime start, call `PlayServ.Connect()`.

### Option B: configure from code

```csharp
using Playserv.Wrapper;

PlayServ.Config(new PlayServSettings
{
    ClientToken = "pk_...",
    GameId = "game-001",
    UserId = "player-001",
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
            GameId = "game-001",
            UserId = "player-001",
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

## 4) Publish and subscribe events

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

    public void SendGlobal(string text)
    {
        PlayServEvents.Publish(new ChatMessageEvent
        {
            FromUserId = "player-001",
            Text = text
        });
    }

    public void SendToGroup(string groupName, string text)
    {
        PlayServEvents.PublishForGroup(groupName, new ChatMessageEvent
        {
            FromUserId = "player-001",
            Text = text
        });
    }

    public void SendToUser(string userId, string text)
    {
        PlayServEvents.PublishForUser(userId, new ChatMessageEvent
        {
            FromUserId = "player-001",
            Text = text
        });
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
    private IDisposable _sharedDisposable;

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
        _shared.Terminated += OnTerminated;

        // Public return type does not include Dispose, but runtime object supports IDisposable.
        if (_shared is IDisposable disposable)
            _sharedDisposable = disposable;
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

        _sharedDisposable?.Dispose();
        _sharedDisposable = null;
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
timeout, request ID, and coalescing key. New gateways correlate by request ID;
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
