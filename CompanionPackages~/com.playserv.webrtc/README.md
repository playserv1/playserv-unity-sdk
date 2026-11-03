# PlayServ WebRTC

Optional WebRTC data-channel transport for `com.playserv.sdk`. The current peer
connection implementation targets WebGL and uses the browser WebRTC API. The
package also contains PlayServ signaling integration.

## Install

Install the core SDK first, then add this package to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.playserv.sdk": "git@github.com:playserv1/playserv-unity-sdk.git#<tag-or-commit>",
    "com.playserv.webrtc": "git@github.com:playserv1/playserv-unity-sdk.git?path=/CompanionPackages~/com.playserv.webrtc#<tag-or-commit>"
  }
}
```

For a scoped registry, install `com.playserv.sdk` and `com.playserv.webrtc` at
compatible versions. Git installations must use the same tag or commit for both
packages.

## Configure

1. Open `Tools > PlayServ > Settings > SDK module settings`.
2. Keep `Client Execution` enabled and enable `WebRTC`.
3. Configure the WebRTC signaling address, data-channel label and ICE servers
   in the PlayServ environment settings.
4. Use a `webrtc://` endpoint when configuring the runtime connection.
5. Build for WebGL.

The package owns `webjl_rtc.jslib`; projects that do not install this package no
longer import the WebRTC browser plugin.

Native Unity peer connections are not implemented yet. Editor and native
platforms can compile the package, but a data-channel connection is supported
only by WebGL builds.
