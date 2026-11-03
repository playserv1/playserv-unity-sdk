# PlayServ Steam Auth

Optional Steamworks.NET adapter for `com.playserv.sdk`. The package discovers an
already installed Steamworks.NET runtime through reflection; it does not bundle
or declare Steamworks.NET as a package dependency.

## Install

Install the core SDK, Steamworks.NET, and this package at the same PlayServ
revision:

```json
{
  "dependencies": {
    "com.playserv.sdk": "git@github.com:playserv1/playserv-unity-sdk.git#<revision>",
    "com.playserv.steam-auth": "git@github.com:playserv1/playserv-unity-sdk.git?path=/CompanionPackages~/com.playserv.steam-auth#<revision>"
  }
}
```

Steamworks.NET initialization, shutdown, and `SteamAPI.RunCallbacks()` pumping
remain game responsibilities. This bridge was checked against Steamworks.NET
`2025.164.0`.

## Use

```csharp
using Playserv.Wrapper;

if (!PlayServSteamAuth.IsAvailable || !PlayServSteamAuth.IsInitialized)
    return;

PlayServAuthResult login = await PlayServSteamAuth.LoginAsync(
    PlayServExternalLoginMode.PreserveCurrentPlayer);

// Add Steam to the current managed player instead:
// PlayServAuthResult link = await PlayServSteamAuth.LinkAsync();
```

The high-level helpers acquire a fresh `GetAuthTicketForWebApi` ticket and
cancel it after PlayServ responds. For manual composition, dispose the
credential yourself:

```csharp
using (var credential = await PlayServSteamAuth.GetCredentialAsync())
{
    if (credential.TryCreateBackendProof(out var proof))
        await PlayServAuth.LoginExternalAsync(proof);
}
```

The bridge passes Steam's optional Web API identity as `null`, matching the
current backend verification call. It correlates the callback by ticket handle
and encodes only the reported ticket bytes as lowercase hexadecimal. Tickets
are never logged or persisted by the package.

`IsAvailable` is `false` when the required Steamworks.NET shape is missing.
Provider acquisition failures throw `PlayServSteamAuthException`; once a proof
exists, PlayServ HTTP/auth failures use `PlayServAuthResult`.

## Tests

Add `com.playserv.steam-auth` to the project's `testables` array to run the
package tests with Unity Test Framework.
