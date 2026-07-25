# PlayServ Google Sign In

Optional Google Sign-In integration for `com.playserv.sdk`. Firebase is not
required or included. A compatible Google Sign-In Unity provider plugin must be
installed separately in the game project.

## Install

Install the core SDK first, then add this package to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.playserv.sdk": "git@github.com:playserv1/playserv-unity-sdk.git#<tag-or-commit>",
    "com.playserv.google-signin": "git@github.com:playserv1/playserv-unity-sdk.git?path=/CompanionPackages~/com.playserv.google-signin#<tag-or-commit>"
  }
}
```

For a scoped registry, install `com.playserv.sdk` and
`com.playserv.google-signin` at compatible versions. Git installations must use
the same tag or commit for both packages.

## Configure

1. Configure the OAuth consent screen in Google Cloud.
2. Create a Web OAuth client and copy its client ID.
3. Create Android and/or iOS OAuth clients for the game's package identifiers.
4. Install a compatible Google Sign-In Unity provider plugin.
5. Run the provider plugin's Android/iOS dependency resolver.
6. Open `Tools > PlayServ > Settings > SDK module settings`.
7. Keep `Client Execution` enabled and enable `Google Sign In`.
8. Return to the main PlayServ window and create/select Google settings.
9. Set the Web client ID and required token, auth-code and scope options.

The settings asset is stored at
`Assets/Resources/PlayServGoogleSignInSettings.asset`.

## Use

```csharp
using Playserv.Wrapper;

if (PlayServGoogleSignIn.IsAvailable)
{
    var credential = await PlayServGoogleSignIn.SignInAsync();
    var idToken = credential.IdToken;
    var authCode = credential.AuthCode;
}
```

`IsAvailable` is `false` when the provider plugin is absent. Provider
credentials remain untrusted until the backend validates their signature,
audience, issuer and expiry.

## Tests

Add `"com.playserv.google-signin"` to the project's `testables` array to run the
package tests with Unity Test Framework.
