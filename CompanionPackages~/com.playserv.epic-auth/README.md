# PlayServ Epic Auth

Optional EOS-Contrib/PlayEveryWare adapter for `com.playserv.sdk`. The package
uses reflection and neither bundles nor declares the EOS plugin as a dependency.

## Install

Install the core SDK, EOS-Contrib, and this package at the same PlayServ
revision:

```json
{
  "dependencies": {
    "com.playserv.sdk": "git@github.com:playserv1/playserv-unity-sdk.git#<revision>",
    "com.playserv.epic-auth": "git@github.com:playserv1/playserv-unity-sdk.git?path=/CompanionPackages~/com.playserv.epic-auth#<revision>"
  }
}
```

The game owns EOS initialization and EAS login. This bridge was checked against
EOS-Contrib `6.1.1`.

## Use an EAS access token

```csharp
using Playserv.EpicAuth;
using Playserv.Wrapper;

PlayServAuthResult login = await PlayServEpicAuth.LoginAsync(
    PlayServEpicAuthRequest.Eos(),
    PlayServExternalLoginMode.PreserveCurrentPlayer);

// Supply an explicit Epic Account ID when more than one local EAS user exists:
// await PlayServEpicAuth.LinkAsync(PlayServEpicAuthRequest.Eos(epicAccountId));
```

With no ID, the bridge asks `EOSManager` for its local Epic Account ID and
copies that user's EAS access token from the EOS Auth interface.

## Use an Epic Games Launcher exchange code

```csharp
PlayServAuthResult login = await PlayServEpicAuth.LoginAsync(
    PlayServEpicAuthRequest.Launcher());
```

`Launcher()` reads `authPassword` from
`GetCommandLineArgsFromEpicLauncher()`. Pass `Launcher(exchangeCode)` to supply
it explicitly. The credential automatically selects PlayServ's
`launcher_exchange_code` provider mode.

An EOS Product User ID belongs to the EOS Connect identity path; it is not an
Epic Account ID and must not be passed to `Eos(epicAccountId)`. Provider tokens
are never logged or persisted by this package.

`IsAvailable` is `false` for a missing or incompatible EOS plugin.
Provider acquisition failures throw `PlayServEpicAuthException`; after proof
creation, PlayServ HTTP/auth failures use `PlayServAuthResult`.

## Tests

Add `com.playserv.epic-auth` to the project's `testables` array to run the
package tests with Unity Test Framework.
