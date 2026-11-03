# Changelog

## [Unreleased]

## [0.7.2] - 2026-11-03

Release prepared locally; distribution tags and registry publication are separate.

### Changed

- Synchronize package and internal PlayServ dependency versions at `0.7.2` (PSV-3431). Rebuild the shipped Schema Tool with matching assembly and version output.

## [0.7.1] - 2026-10-23

Release prepared locally; distribution tags and registry publication are separate.

### Changed

- Synchronize package and internal PlayServ dependency versions at `0.7.1` (PSV-3254). No companion API changes.

## [0.7.0] - 2026-10-22

Release prepared locally; distribution tags and registry publication are separate.

### Changed

- Synchronize package and internal PlayServ dependency versions at `0.7.0` (PSV-3216). Rebuild the shipped tool with matching source, assembly and version output.

## [0.6.11] - 2026-10-08

Release prepared locally; distribution tags and registry publication are separate.

### Changed

- Align package and internal PlayServ dependency versions with `0.6.11` (PSV-3089); no companion API changes.

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
- Rebuilt the shipped Schema Tool with matching source, assembly and `version --json` metadata.

## [0.5.1] - 2026-09-12

- Aligned the package and internal PlayServ dependency versions with `0.5.1`.

## [0.5.0] - 2026-09-12

- Aligned the package and internal PlayServ dependency versions with `0.5.0`.

- Added atomic code-first schema pushes with server-key CLI exchange,
  whole-schema revision preflight, offline dry-run validation, and expanded
  entity/field metadata attributes matching the shipped Schema Service.

## [0.4.1] - 2026-08-23

- Restored Unity 2021.3 Linux compatibility by targeting its bundled .NET 5
  runtime while retaining roll-forward support for newer Unity editors.
- Replaced platform-specific SDK Roslyn binaries with portable NuGet
  assemblies so schema analysis runs on Linux and macOS Unity editors.
- Preserved the published dependency manifest so Unity 2021 Linux resolves the
  shipped Roslyn support assemblies instead of incompatible framework copies.
- Aligned the external tool, package, and core SDK dependency versions with
  `0.4.1`.

## [0.4.0] - 2026-08-20

- Aligned the external tool, package, and core SDK dependency versions with
  `0.4.0`.

## [0.3.9] - 2026-08-19

- Aligned the external tool, package, and core SDK dependency versions with
  `0.3.9`.

## [0.3.8] - 2026-08-17

- Aligned the package and core SDK dependency versions with `0.3.8`.

## [0.3.7] - 2026-08-12

- Aligned the external tool, package, and core SDK dependency versions with
  `0.3.7`.

## [0.3.6] - 2026-08-12

- Aligned the external tool, package, and core SDK dependency versions with
  `0.3.6`.

## [0.3.4] - 2026-07-25

- Aligned the external tool, package, and core SDK dependency versions with
  `0.3.4`.

## [0.3.3] - 2026-07-25

- Added the external schema CLI, Roslyn contract analysis, deterministic
  generation, filesystem watch mode, and Unity/Rider launch integration.
