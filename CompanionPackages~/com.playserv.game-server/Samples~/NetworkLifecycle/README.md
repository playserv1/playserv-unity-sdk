# TCP network lifecycle sample (PSV-3726)

Import **TCP Network Lifecycle** from the Game Server companion's Samples list in
Package Manager. The sample uses ordinary .NET TCP sockets and works in the Editor,
desktop players and dedicated servers. It is a small transport example, not a
WebGL connector or the C# game-server WebSocket protocol. The sample has no TLS;
run it on loopback or a trusted development network. Use a protected game transport
for real player tickets on an untrusted network.

1. Enable the Game Server companion. Add `PlayServNetworkLifecycleSample` to one
   root GameObject in the dedicated server's startup scene. Configure the platform
   launch as described in the companion's hosted-room guide, with a TCP listen port
   and public TCP mapping. Start the server. It logs the assigned room name only
   after the room and adapter are ready.
2. In a separate desktop client or Editor project, configure and sign in through
   the normal PlayServ client SDK. Add `PlayServTcpRoomClientSample` to a root object,
   and set the function slug and assigned room name. In Play mode, invoke **Join
   room** from the component's context menu. A fresh ticket is requested on each Join;
   tickets are never Inspector fields or log output. Invoke **Send gameplay ping**
   to verify traffic is attributed to the admitted player.
3. Invoke **Drop connection (reconnect grace)**, then **Join room** before the
   30-second grace expires. The new socket gets a new connection object and a fresh
   ticket. The room keeps one player in presence. Let grace expire to test departure.
4. Call the server component's `RemovePlayer(verifiedPlayerId)` to kick immediately.
   Call `ChangeScene(sceneName)` with another scene in the build to verify that the
   live connection, adapter and player binding survive. Do not add a second server
   component in the destination scene. Scene loads do not pause a real disconnect's
   grace or permit replay of an old ticket.
5. Await `ShutdownAsync()` before intentionally exiting the server process. Unity
   does not await `OnDestroy`; that callback is best effort only. Operator callbacks
   in this sample close real sockets before confirming their work.

The first newline-delimited UTF-8 line is the reservation token (maximum 4096 bytes).
Authentication must finish within 10 seconds, and the listener caps connections at
64. A successful Accept produces `ready <verified-player-id>`. Only then is the
sample `ping` command accepted (128-byte maximum), answered as `pong <player-id>`.
Unknown commands, malformed/oversized frames, timeouts and refused admission close
the socket. Before the adapter attaches, authentication is refused; no connection
may bypass the gameplay gate. The transport raises Disconnected on socket closure;
the adapter decides grace versus explicit removal.

The imported **EditMode** fixture `PlayServTcpRoomTransportTests` runs real loopback
sockets without platform access: it checks the pre-admission gate, admitted traffic,
rejection, oversized input and shutdown during authentication. SDK PlayMode tests
exercise the adapter with the real pushed-admission implementation and a platform
wire fixture, including reconnect, late rejection, scene changes and cleanup.
Neither suite claims a live platform deployment or a two-player network acceptance run.
