# Changelog

## [Unreleased]

## [0.5.1] - 2026-09-12

- Aligned the package and internal PlayServ dependency versions with `0.5.1`.

## [0.5.0] - 2026-09-12

- Aligned the package and internal PlayServ dependency versions with `0.5.0`.

- Removed legacy runtime game/user identity inputs from the connection popup;
  the optional deployment ID is retained only for Editor version tooling.

## [0.4.1] - 2026-08-23

- Aligned the package, core SDK, Analytics, and Game Server dependency versions
  with `0.4.1`.

## [0.4.0] - 2026-08-20

- Added public platform status, runtime table catalogue, bulk Records, realtime
  refresh, OR/nested Include, binary Cloud Function, refcounted event-observer,
  and unified matchmaking-error commands.
- Added an Editor/Dedicated Server-only command extension for Game Server
  configuration, rooms, realtime/acting-player Records, matchmaking,
  reservation admission, JWT validation, player lookup, Code, Analytics and
  Commerce. Server keys, JWTs and reservation tokens use the memory-only secure
  modal and are excluded from command history and output.
- Preserved the dedicated-server command bootstrap in IL2CPP builds through an
  explicit Unity linker declaration.
- Aligned the package, core SDK, Analytics, and Game Server dependency versions
  with `0.4.0`.

## [0.3.9] - 2026-08-19

- Added terminal coverage for Analytics, Cloud Functions, Catalog, Storefront,
  complete player matchmaking, runtime Data ACL capabilities, and realtime
  `PlayServRecord<T>` handles.
- Added pagination state, operation status/cancellation, strict JSON validation,
  record-watch lifecycle cleanup, response preview limits, and reservation-token
  redaction.
- Added a typed dependency on `com.playserv.analytics`.
- Aligned the package, core SDK, and Analytics dependency versions with `0.3.9`.

## [0.3.8] - 2026-08-17

- Added the optional Debug Terminal module.
- Moved the authenticated terminal scene out of the core SDK sample.
- Split scene-owned connection and terminal components into reusable prefabs.
- Moved the terminal parser, command engine, models, and components into the
  package runtime assembly while preserving scene and prefab script GUIDs.
- Added grouped SDK diagnostics, identity lifecycle, Typed Records V2,
  deterministic subscription management, awaitable RPC, raw events, and spawn
  scope commands while retaining the original flat command set.
- Added secure in-memory credential prompts, structured error history, grouped
  autocomplete, query validation, and package-owned EditMode/PlayMode tests.
