# Changelog

## [Unreleased]

## [0.3.6] - 2026-08-12

- Marked the UDP and RUDP transport assemblies with `AlwaysLinkAssembly` so
  their automatic registration survives managed code stripping in IL2CPP builds.

## [0.3.4] - 2026-07-25

- Extracted UDP and RUDP from the core SDK into one optional companion UPM
  package.
- Added independent module toggles for UDP and RUDP.
