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

### Changed

- Aligned the package and internal PlayServ dependency versions with `0.6.0` (PSV-2694).

## [0.5.1] - 2026-09-12

- Aligned the package and internal PlayServ dependency versions with `0.5.1`.

## [0.5.0] - 2026-09-12

- Aligned the package and internal PlayServ dependency versions with `0.5.0`.

## [0.4.1] - 2026-08-23

- Aligned the package and core SDK dependency versions with `0.4.1`.

## [0.4.0] - 2026-08-20

- Added dedicated-server profile support and an internal per-event identity
  path used by `com.playserv.game-server` without mutable global player state.
- Aligned the package and core SDK dependency versions with `0.4.0`.

## [0.3.9] - 2026-08-19

- Changed the default provider to send bounded batches through the shipped
  `POST /analytics/events` runtime endpoint. Custom provider overrides remain
  supported and retain precedence across reconnects.
- Aligned the package and core SDK dependency versions with `0.3.9`.

## [0.3.8] - 2026-08-17

- Aligned the package and core SDK dependency versions with `0.3.8`.

## [0.3.7] - 2026-08-12

- Aligned the package and core SDK dependency versions with `0.3.7`.

## [0.3.6] - 2026-08-12

- Marked the runtime module assembly with `AlwaysLinkAssembly` so its automatic
  registration survives managed code stripping in IL2CPP player builds.
- Made client-defined analytics providers configurable before PlayServ
  connection setup and persistent across runtime reconnects.
- Added `HasCustomProvider` and `ResetProvider()` without adding a dependency on
  Firebase or another analytics vendor.

## [0.3.4] - 2026-07-25

- Extracted PlayServ Analytics from the core SDK into an optional companion UPM
  package.
- Moved the Analytics runtime tests into the companion package.
