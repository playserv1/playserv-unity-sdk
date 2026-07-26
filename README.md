# PlayServ Unity SDK

PlayServ is a modular multiplayer SDK for Unity. The core package provides
runtime connection management, data subscriptions, typed events, RPC, spawning,
and the built-in HTTP/WebSocket transport. Analytics, identity providers,
native transports, WebRTC, Pulse, and schema tooling are optional companion
packages.

## Requirements

- Unity 2021.3 or newer.
- A PlayServ game ID and runtime credential.
- `com.unity.nuget.newtonsoft-json` 3.2.2. Unity Package Manager installs this
  dependency automatically for UPM installations.

## Install

In Unity, open `Window` -> `Package Manager`, choose `Add package from git URL`,
and enter your repository URL with a tag or commit:

```text
https://<git-host>/<organization>/<repository>.git#<tag-or-commit>
```

For a package registry, add `com.playserv.sdk` to the project's
`Packages/manifest.json`.

## Companion packages

Optional PlayServ features are distributed as separate packages. With the Git
repository, install core first and add only the required packages:

```json
{
  "dependencies": {
    "com.playserv.sdk": "git@github.com:playserv1/playserv-unity-sdk.git#<tag-or-commit>",
    "com.playserv.apple-signin": "git@github.com:playserv1/playserv-unity-sdk.git?path=/CompanionPackages~/com.playserv.apple-signin#<tag-or-commit>",
    "com.playserv.google-signin": "git@github.com:playserv1/playserv-unity-sdk.git?path=/CompanionPackages~/com.playserv.google-signin#<tag-or-commit>",
    "com.playserv.webrtc": "git@github.com:playserv1/playserv-unity-sdk.git?path=/CompanionPackages~/com.playserv.webrtc#<tag-or-commit>",
    "com.playserv.analytics": "git@github.com:playserv1/playserv-unity-sdk.git?path=/CompanionPackages~/com.playserv.analytics#<tag-or-commit>",
    "com.playserv.pulse": "git@github.com:playserv1/playserv-unity-sdk.git?path=/CompanionPackages~/com.playserv.pulse#<tag-or-commit>",
    "com.playserv.transports-native": "git@github.com:playserv1/playserv-unity-sdk.git?path=/CompanionPackages~/com.playserv.transports-native#<tag-or-commit>",
    "com.playserv.schema-tool": "git@github.com:playserv1/playserv-unity-sdk.git?path=/CompanionPackages~/com.playserv.schema-tool#<tag-or-commit>"
  }
}
```

Only add the companion packages used by the game. Unity package dependencies
cannot contain Git URLs, so private Git installations must declare core and
each companion package directly in the project manifest. Use the same tag or
commit for core and every companion package.

Runtime companion packages can also be managed from `Tools` -> `PlayServ` ->
`Settings` -> `SDK module settings`. They stay visible there even when they are
not installed. `Install` and `Remove Package` update the project through Unity
Package Manager. `Installed` and `Enabled` are separate states: disabling an
installed module excludes its assembly through a scripting define without
removing the package. The Native Transports package exposes separate toggles for
UDP and RUDP.

The `Schema Workflows` section in the main PlayServ window exposes both schema
directions. `Local contracts` installs the external UPM Schema Tool to analyze
`[PlayServSchema]` C# contracts, generate JSON Schema and backend DTOs, validate
drift in CI, and watch project files. `Server schema` downloads the selected
project schema from the PlayServ Schema API, previews its difference from the
accepted schema, and generates Unity C# models only after explicit
confirmation.

## Cache maintenance

When the installed SDK version or PlayServ cache schema changes, the editor
automatically clears PlayServ generated state, the shared codegen cache, and
inactive `Library/PackageCache/com.playserv.*` directories. Active package
caches and unrelated Unity caches are preserved.

Use `Tools` -> `PlayServ` -> `Cache` -> `Clear PlayServ Cache` for a manual
targeted cleanup. `Rebuild Project Library...` is the recovery option for a
corrupted Unity cache: after confirmation it closes Unity, deletes the complete
project `Library` directory, and reopens the project.

## Configure

Open `Tools` -> `PlayServ` -> `Settings`, select an environment and configure the
game ID, user ID, game version, and runtime credential.

```csharp
using Playserv.Wrapper;

PlayServ.Config(new PlayServSettings
{
    ClientToken = "pk_...",
    GameId = "your-game-id",
    UserId = "player-id",
    GameVersion = "1.0.0",
    BackendServerAddress = "wss://your-playserv-endpoint/ws"
});

bool connected = await PlayServ.Connect();
```

For authenticated players, provide the JWT only at runtime:

```csharp
using Playserv.Runtime.Abstractions;

PlayServ.SetRuntimeTokenProvider(
    new PlayServDelegateRuntimeTokenProvider(ct => sessionService.GetPlayServJwtAsync(ct)));
```

Never put `sk_*` keys or player JWTs in `PlayServConfig`. Deployment credentials
are read by Editor tools from `PLAYSERV_DEPLOY_AUTH_TOKEN` or project-scoped local
Editor storage.

Optional modules are enabled per project and stored in
`ProjectSettings/PlayServModules.json`.

## Analytics

Install `com.playserv.analytics`, enable `Analytics` in `Tools` -> `PlayServ` ->
`Settings` -> `SDK module settings`, connect PlayServ, and track explicit
gameplay events:

```csharp
using System.Collections.Generic;
using Playserv.Wrapper;

PlayServAnalytics.SetUserProperty("role", "parent");
PlayServAnalytics.Track(
    "login_sso_success",
    new Dictionary<string, object>
    {
        { "provider", "google" },
        { "attempt", 1 },
        { "new_user", true }
    });
```

Events are queued in memory, enriched with PlayServ user/session/app context,
and sent in bounded batches. The default provider requires a backend handler for
`module_analytics.TrackAnalyticsBatch`; applications can install a custom
`IPlayServAnalyticsProvider` while PlayServ ingestion is being deployed.

## Samples

Select PlayServ SDK in Package Manager and import **PlayServ SDK Examples**.
The sample contains connection setup, data subscription, events, RPC, and spawn
scenes without compiling example code into projects that do not import it.

## Documentation

- [Complete Unity SDK guide](Documentation~/playserv-sdk.md)
- [Server/shared runtime guide](Documentation~/server-sdk.md)
- [Analytics setup](Documentation~/playserv-sdk.md#analytics-module)
- [External Schema Tool](Documentation~/playserv-sdk.md#external-schema-tool)
- [Apple Sign In setup](Documentation~/playserv-sdk.md#apple-sign-in-module)
- [Google Sign In setup](Documentation~/playserv-sdk.md#google-sign-in-module)
- [Changelog](CHANGELOG.md)

## Tests

Package tests live under `Tests/Editor` and `Tests/Runtime`. Add
`"com.playserv.sdk"` to the consuming project's `testables` array before running
them with Unity Test Framework. The complete guide contains batch-mode commands.

## License

PlayServ SDK is available under the [MIT License](LICENSE.md). Third-party
components are listed in [Third Party Notices](Third%20Party%20Notices.md).
