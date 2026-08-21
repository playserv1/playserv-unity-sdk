# PlayServ Facebook Login

Optional Meta Unity SDK Limited Login adapter for `com.playserv.sdk`. The
package discovers an already installed Meta SDK through reflection and does not
bundle or declare it as a dependency.

## Install

Install the core SDK, Meta Unity SDK, and this package at the same PlayServ
revision:

```json
{
  "dependencies": {
    "com.playserv.sdk": "git@github.com:playserv1/playserv-unity-sdk.git#<revision>",
    "com.playserv.facebook-login": "git@github.com:playserv1/playserv-unity-sdk.git?path=/CompanionPackages~/com.playserv.facebook-login#<revision>"
  }
}
```

The game must initialize `FB` before calling the facade. This bridge was checked
against Meta Unity SDK `18.1.0`.

## Use

```csharp
using Playserv.FacebookLogin;
using Playserv.Wrapper;

var request = new PlayServFacebookLoginRequest(
    permissions: new[] { "public_profile", "email" });

PlayServAuthResult login = await PlayServFacebookLogin.LoginAsync(
    request,
    PlayServExternalLoginMode.PreserveCurrentPlayer);

// Add Facebook to the current managed player instead:
// PlayServAuthResult link = await PlayServFacebookLogin.LinkAsync(request);
```

Only `FB.Mobile.LoginWithTrackingPreference(LoginTracking.LIMITED, ...)` is used. When the
request omits a nonce, the bridge creates a cryptographically secure 32-byte
base64url nonce. The callback must return an authentication token bound to the
same nonce before any PlayServ HTTP call begins. Access tokens from regular
Facebook Login are not accepted by this adapter.

Authentication tokens and nonces are not logged or persisted by the package.
`IsAvailable` is `false` for a missing or incompatible Meta SDK, while
`IsInitialized` also requires `FB.Init` to have completed. Provider acquisition
failures throw `PlayServFacebookLoginException`; after proof creation, PlayServ
HTTP/auth failures use `PlayServAuthResult`.

## Tests

Add `com.playserv.facebook-login` to the project's `testables` array to run the
package tests with Unity Test Framework.
