# Changelog

## [Unreleased]

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
