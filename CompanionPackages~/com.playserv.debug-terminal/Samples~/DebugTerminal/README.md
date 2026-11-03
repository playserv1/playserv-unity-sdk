# PlayServ Debug Terminal Sample

Open `DebugTerminal.unity` and enter Play Mode. The connection popup is filled
from the active `PlayServConfig` and selected environment. Values edited in the
popup apply only to the current session.

The scene is intentionally small and contains two prefab instances:

- `Prefabs/PlayServ Connection.prefab`
- `Prefabs/PlayServ Debug Terminal.prefab`

The terminal remains hidden until anonymous player authentication and the
PlayServ transport handshake succeed. Type `help` to list the available
commands. Press the backquote key to show or hide the terminal.

Useful smoke-test commands:

```text
sdk info
sdk modules
identity session
analytics status
analytics track level_started level=3 mode=ranked
code call health
code bytes GET generated-asset --max-response-bytes 16777216
platform current
table list
catalog list --limit 10
storefront list --limit 10
match find ranked --wait_ms 5000 --params '{"mode":"duo"}'
record query --sort level --desc --limit 10
record loadall --max-records 100
record batch status
record capabilities
record watch
subscription status
subscription refresh all
event subscribe typed 2
event status
rpc await hello
spawn scope
errors 10
server configure --backend https://api.example.com --prompt
server status
server realtime connect
record caller server
server room status
```

Identity login/link/merge and `auth refresh` collect credentials in a secure
modal. Do not paste credentials into the command line. Credentials are kept in
memory only and cleared when the modal closes.

Typed record commands target a backend entity named `Player` with `Nickname`
and `Level` fields. `record loadorcreate` additionally expects `Nickname` to be
configured as a unique field. The sample RPC targets
`NotificationService.BroadcastToAll`, and spawn commands expect `TestCube` (or
the requested asset) under `Resources` with a compatible `NetworkObject`.

The package root `README.md` contains the full command reference, query options,
security rules, and troubleshooting guide.

The package has typed dependencies on `com.playserv.analytics` and
`com.playserv.game-server`. Analytics, Client Execution, Data Subscription,
Events, RPC, and Spawn must be enabled in `Tools > PlayServ > Settings`.
Dedicated-server commands are available only in the Editor or a
`UNITY_SERVER` build. Enter server keys, player JWTs and reservation tokens only
through the secure modal; never paste them into a terminal command.
