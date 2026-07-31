# Changelog

## [Unreleased]

- Made client-defined analytics providers configurable before PlayServ
  connection setup and persistent across runtime reconnects.
- Added `HasCustomProvider` and `ResetProvider()` without adding a dependency on
  Firebase or another analytics vendor.

## [0.3.4] - 2026-07-25

- Extracted PlayServ Analytics from the core SDK into an optional companion UPM
  package.
- Moved the Analytics runtime tests into the companion package.
