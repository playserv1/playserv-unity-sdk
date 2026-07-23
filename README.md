# PlayServ Unity SDK

PlayServ is a modular multiplayer SDK for Unity. It provides runtime connection
management, data subscriptions, typed events, RPC, spawning, multiple transports,
and optional Apple and Google sign-in modules.

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

Optional modules are enabled per project and stored in
`ProjectSettings/PlayServModules.json`.

## Samples

Select PlayServ SDK in Package Manager and import **PlayServ SDK Examples**.
The sample contains connection setup, data subscription, events, RPC, and spawn
scenes without compiling example code into projects that do not import it.

## Documentation

- [Complete Unity SDK guide](Documentation~/playserv-sdk.md)
- [Server/shared runtime guide](Documentation~/server-sdk.md)
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
