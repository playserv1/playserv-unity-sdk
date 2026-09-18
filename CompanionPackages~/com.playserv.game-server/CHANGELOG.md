# Changelog

## [Unreleased]

## [0.6.2] - 2026-09-18

Release prepared locally; distribution tags and registry publication are separate.

### Changed

- Aligned package and internal PlayServ dependency versions with `0.6.2`; no companion API changes.

## [0.6.1] - 2026-09-18

Release prepared locally; distribution tags and registry publication are separate.

### Changed

- Aligned package and internal PlayServ dependency versions with `0.6.1`; no companion API changes.

## [0.6.0] - 2026-09-17

- 2026-09-17 (PSV-2694): Documented the core player Host API as the caller of the
  existing requested-room factory; no companion runtime behavior changed.

- 2026-09-17 (PSV-2556): Added explicit connection-bound `Uplink.Data` queries and
  send-only upsert/increment/delete after backend PSV-2683. Includes server-catalogue
  ACL pre-checks, snapshotted JSON, correlated bounded queries and disconnect cleanup.
  Existing Records REST and `/ws` behavior is unchanged. Send completion is not
  persistence acknowledgement; data subscriptions and server write-ACL enforcement
  remain limitations. Verification uses fixtures, not a live backend run.

- 2026-09-15 (PSV-2628): Added copied `RoomFactory` context attributes and the
  `ContentRefused` decision for player-requested room content. Flat/2048-byte input
  validation happens before dispatch; only the game's returned snapshot is registered.
  Existing factories and frames without attributes keep their behavior. Backend
  senders are implemented; this change is fixture-tested, not live integration evidence.

- 2026-09-14: Aligned room creation refusals with uplink §1.3: active/pending quota
  returns `room_quota_exceeded`; thrown/faulted factories return `room_create_failed`
  with a bounded exception type name only. Neither refusal drains the instance.
  Factory failures release the name and preserve local completion diagnostics;
  SDK cancellation and registration failures do not produce late/duplicate results.

- 2026-09-14: Opt-in typed inbound server RPC registry, authenticated caller metadata, bounded serial dispatch, cancellation and AOT-preserved sample. No implicit Records authority or automatic replay.

- 2026-09-14: Explicit runtime schema advisory for caller-visible table presence; no schema mutations or initialization gate.

- 2026-09-14: Added `PlayServServerLaunch` helpers for neutral launch environment metadata with explicit-value precedence and safe validation.

- 2026-09-14: Added strict `GetRoomConfigurationAsync` and Unity-context uplink lifecycle events; standalone reads never overwrite live configuration.

- 2026-09-14: Added room connection tracker with a configurable 30-second reconnect grace and fresh-ticket replacement for parked players. Manual presence is unchanged.

- 2026-09-14: Added `JoinRoomForPlayerAsync` with snapshotted admission parameters, server credentials and strict matched-reservation parsing.

- 2026-09-13: Added protocol-interoperability fixtures for session renewal,
  receipt-clock expiry, room-create registration with early ticket offers, and
  terminal cleanup without event subscribers. Backend half-life renewal is now
  served; real platform-requested room creation still requires PSV-2590.

- 2026-09-13 (PSV-2556 stage two): Added pushed-ticket admission, local TTL/replay
  guards, bounded offer storage, token-bearing joins, rejection callbacks and releases.
  **Default behavior change:** `EnablePushedAdmission` is true. Migrate games to
  `room.Admission` and wire game-owned disconnection, or explicitly set it false
  to retain REST consume. No production route or package-version change.

- 2026-09-12 (PSV-2602): Moved room list, registration/heartbeat, close and launch
  to the served `/rooms/...` namespace. Find/consume routes and credentials stay unchanged.
  Added server reservation endpoint/region/attribute snapshots, optional `ExpiresIn`
  and monotonic `RemainingLifetime`; covered strict TTL validation and typed consume verdicts.

- 2026-09-12: Added opt-in, pre-redacted structured logs with count/byte-bounded
  queues, single-flight flush accounting and independent shutdown LogsError.

- 2026-09-12: Added uplink score submission and correlated Top/viewer leaderboard
  reads over the existing project-level score store, without automatic retries.

- 2026-09-12: Added typed server-to-client event publishing over an already
  connected uplink, with bounded frames and no implicit delivery acknowledgement.

- 2026-09-12: Added online player-session verification with typed verdicts,
  rotating server/session credentials and secret-safe errors.

### Added

- 2026-09-12: PSV-2597 SDK-side `room_create` capability, cancellable Unity-thread
  room factory, bounded response/registration, duplicate guards and safe completion
  outcomes. Platform dispatch/integration remains dependent on PSV-2590.

- 2026-09-12: PSV-2556 stage-one dial-in uplink, upgrade-only credentials,
  in-memory REST sessions, reconnect, authoritative room configuration, presence
  checksums/repair and lifetime/idle shutdown. Records realtime intentionally stays
  on the separate `sk_*` `/ws` session. Pushed admission is added in stage two above;
  REST room routes are migrated by PSV-2602 above.

## [0.5.1] - 2026-09-12

- Aligned the package and internal PlayServ dependency versions with `0.5.1`.

## [0.5.0] - 2026-09-12

- Aligned the package and internal PlayServ dependency versions with `0.5.0`.

- **Breaking:** removed the legacy `GameId` field from dedicated-server
  realtime options; the rotating `sk_*` credential identifies the project.

## [0.4.1] - 2026-08-23

- Aligned the package, core SDK, and Analytics dependency versions with `0.4.1`.

## [0.4.0] - 2026-08-20

- Added server-key Analytics with bounded retry-safe batching, per-event player
  attribution, graceful-shutdown flush reporting, and typed Catalog/Storefront
  read facades with cursor pagination.
- Added server-scoped table catalogue list, lookup, and forced-refresh APIs
  backed by the same metadata cache used by typed Records.
- Added credential-free cached JWKS validation for RS256 player session JWTs,
  including issuer, lifetime, project/environment scope, safe claims, and one
  forced key refresh on unknown `kid`.
- Added opt-in dedicated-server realtime Records over an independent rotating
  `sk_*` WebSocket session, including shared collection/record handles,
  refresh, refcounted close, reconnect replay, and shutdown cleanup.
- Added `PlayServGameServer.AsPlayer(playerJwt)` for attributing player-owned
  Records writes while retaining server-authorized read scope.
- Added server-key typed Records CRUD/query/singleton access with `acl.server`
  capability checks and rotating credentials.
- Added server-key Cloud Functions invocation through `PlayServGameServer.Code`.
- Aligned the package, core SDK, and Analytics dependency versions with
  `0.4.0`.

## [0.3.9] - 2026-08-19

- Added the Unity Dedicated Server companion with rotated `sk_*` credentials,
  server matchmaking, room lifecycle, reservation admission, launch, room
  listing, safe player lookup, multi-room heartbeats, and graceful shutdown.
