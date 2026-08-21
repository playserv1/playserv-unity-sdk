# PlayServ Analytics

Optional provider-based gameplay analytics for `com.playserv.sdk`. The package
collects typed events, user and session context, bounded batches, and sends them
to the PlayServ runtime HTTP ingestion endpoint. It does not depend on Firebase.

## Install

Install this package from `Tools > PlayServ > Settings > SDK module settings`,
or add it beside the core SDK in `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.playserv.sdk": "git@github.com:playserv1/playserv-unity-sdk.git#<tag-or-commit>",
    "com.playserv.analytics": "git@github.com:playserv1/playserv-unity-sdk.git?path=/CompanionPackages~/com.playserv.analytics#<tag-or-commit>"
  }
}
```

Git installations must use the same tag or commit for both packages.

## Use

1. Open the PlayServ SDK module settings and enable `Analytics`.
2. Connect the PlayServ client.
3. Record an event with `PlayServAnalytics.Track(...)`.
4. Await `PlayServAnalytics.FlushAsync(...)` before a controlled shutdown when
   delivery of the current batch matters.

Applications can replace the default HTTP provider through
`PlayServAnalytics.SetProvider(...)`. Configure the provider in client project
code before or after `PlayServ.Connect()`; the selection survives PlayServ
runtime reconnects. Call `PlayServAnalytics.ResetProvider()` to return to
PlayServ `POST /analytics/events` delivery.

The SDK deliberately does not include Firebase or another vendor adapter.
Implement `IPlayServAnalyticsProvider` under the client project's `Assets`
folder when events must be routed to a custom ingestion service.

## Dedicated servers

The same queue and typed parameter model can be used by Unity Dedicated Server
through `com.playserv.game-server`. Server events use
`PlayServGameServer.Analytics.Track(..., playerId: ...)`, so player attribution
is local to each event rather than mutable process-wide state. Delivery uses
the rotating `sk_*` credential and `ShutdownAsync` reports a failed final flush.
