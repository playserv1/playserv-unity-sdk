# PlayServ Apple Sign In

Optional native iOS Sign in with Apple module for `com.playserv.sdk`.
It uses `AuthenticationServices.framework` and does not require Firebase or a
third-party authentication SDK.

## Install

Install the core SDK first, then add this package to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.playserv.sdk": "git@github.com:playserv1/playserv-unity-sdk.git#<tag-or-commit>",
    "com.playserv.apple-signin": "git@github.com:playserv1/playserv-unity-sdk.git?path=/CompanionPackages~/com.playserv.apple-signin#<tag-or-commit>"
  }
}
```

For a scoped registry, install `com.playserv.sdk` and
`com.playserv.apple-signin` at compatible versions. Git installations must use
the same tag or commit for both packages.

## Configure

1. Enable Sign in with Apple for the game's App ID in Apple Developer.
2. Make the Unity iOS Bundle Identifier match that App ID.
3. Open `Tools > PlayServ > Settings > SDK module settings`.
4. Keep `Client Execution` enabled and enable `Apple Sign In`.
5. Return to the main PlayServ window and create/select Apple settings.
6. Configure requested scopes, nonce/state, client ID, entitlement filename,
   and automatic Xcode capability setup.
7. Build for iOS and verify the Xcode target contains Sign in with Apple.

The settings asset is stored at
`Assets/Resources/PlayServAppleSignInSettings.asset`.

## Use

```csharp
using Playserv.Wrapper;

if (PlayServAppleSignIn.IsAvailable)
{
    var credential = await PlayServAppleSignIn.SignInAsync();
    var identityToken = credential.IdentityToken;
    var authorizationCode = credential.AuthorizationCode;
}
```

Apple private keys, Team ID, Key ID and generated client secrets belong only on
the backend. Provider credentials remain untrusted until the backend validates
their signature, audience, issuer, expiry and nonce.

## Tests

Add `"com.playserv.apple-signin"` to the project's `testables` array to run the
package tests with Unity Test Framework.
