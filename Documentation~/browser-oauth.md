# Browser sign-in (0.6.3)

`PlayServAuth.LoginBrowserAsync` signs in through the platform's configured browser
provider in WebGL, Editor and desktop builds. It requires the existing platform
completion endpoints; no native provider companion is needed for this flow.
Provider availability/configuration remains server-owned.

Call directly in the button handler, before any `await`, coroutine yield or timer:

```csharp
private CancellationTokenSource signIn;

public async void OnGoogleClicked()
{
    if (signIn != null) return;
    signIn = new CancellationTokenSource();
    try
    {
        var result = await PlayServAuth.LoginBrowserAsync("google",
            new PlayServBrowserLoginOptions
            {
                Timeout = TimeSpan.FromMinutes(5),
                AllowPlayerSwitch = false
            }, signIn.Token);
        if (!result.IsSuccess) ShowError(result.Error.Message);
    }
    catch (OperationCanceledException) { }
    finally { signIn.Dispose(); signIn = null; }
}

public void CancelSignIn() => signIn?.Cancel();
```

In WebGL the SDK reserves a popup synchronously in that call, before the first
HTTP await, then navigates it after `:start`. A blocked popup returns
`BrowserPopupBlocked`; offer another explicit click after the user allows popups.
Editor/desktop open the system browser once the authorize URL is available.
Cancel/finish closes a retained WebGL popup where browser policy permits; desktop
browser windows remain under the user's control. HTTPS (or local development
loopback) and secure browser randomness are required.

The SDK sends PKCE S256 and `completion: platform` to `:start`; only the in-memory
flow keeps the verifier. It polls `:claim` every two seconds, respecting longer
`Retry-After` values within the default five-minute budget. Claim `401` does not
identify a precise cause: the flow may be unfinished or no longer claimable.
The SDK waits until its budget ends. It never retries `:start` automatically.
`BrowserClaimOutcomeUnknown` means the single-use claim response was lost or
unusable; the SDK does not replay that claim or silently begin a new login.
Never log request bodies, authorize URLs, state, verifier or token bundles.

Only one browser flow runs at a time (`BrowserFlowInProgress`). The current
player session and refresh loop continue while the browser is open. Logout,
configuration changes, another auth operation or caller cancellation invalidate
its result. Late claimed bundles are discarded and revocation is attempted with
a bounded request. A successful result uses the SDK's existing session store,
refresh and platform reconnect path. Applications with their own token provider
must continue to own authentication; this API does not replace that provider.

If the verified account belongs to another player, the default refuses to switch
with `BrowserPlayerSwitchRequired` and preserves the current player. Present a
clear account-switch action before invoking a new login with
`AllowPlayerSwitch = true`. This includes switching away from an anonymous player.
Browser linking, identity merge and guest-progress transfer are not provided.
Save any application-owned state before explicitly choosing an account switch.

Local HTTP/browser fixtures validate protocol and lifecycle behavior. Real
provider authorization needs configured provider credentials/callbacks and is a
separate acceptance check; a fixture pass does not certify it.
