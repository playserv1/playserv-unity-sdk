# PlayServ Native Transports

Optional UDP and reliable UDP transports for `com.playserv.sdk`.

The package owns two independently configurable modules:

- `UDP` for `udp://` endpoints.
- `RUDP` for `rudp://` endpoints with acknowledgements and retransmission.

Install or remove the package from the main PlayServ SDK module settings.
Installing it does not force both transports to compile: UDP and RUDP have
separate Enable/Disable toggles and scripting defines.

For Git installations, use the same tag or commit as the core SDK:

```json
"com.playserv.transports-native": "git@github.com:playserv1/playserv-unity-sdk.git?path=/CompanionPackages~/com.playserv.transports-native#<tag-or-commit>"
```

These transports support Editor, Standalone, Android, and iOS. WebGL projects
should use WebSocket or the separate WebRTC companion package.
