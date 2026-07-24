# Changelog

All notable changes to this package will be documented in this file.

The format is based on Keep a Changelog, and this project follows Semantic Versioning.

## [Unreleased]

- Removed serialized runtime authorization and deployment tokens from
  `PlayServConfig`; player JWTs now come from
  `IPlayServRuntimeTokenProvider`, while deploy credentials use
  `PLAYSERV_DEPLOY_AUTH_TOKEN` or project-scoped local Editor storage.
- Added runtime credential validation that accepts only public `pk_*` client
  tokens and rejects `Bearer sk_*` before player connections.
### Added

- Added `Tools > PlayServ > Migrate Project`, a Roslyn-based preview and
  migration tool for replacing removed `PlayServ.*` optional APIs with their
  module-specific facades. Applying changes creates project-local backups and a
  Markdown migration report.
- Added outbound packet diagnostics with per-packet `[PKT-OUT]` names,
  once-per-second WebSocket packet counts, and `[RESP-WIRE]` timing around
  Respawn socket writes.
- Extended `module.playserv.json` schema v1 with minimum SDK version,
  supported platforms, module conflicts, capabilities, and required UPM
  packages.
- Added deterministic topological module ordering and explicit cyclic
  dependency diagnostics.
- Added Unity package tests under `Tests/Editor` and `Tests/Runtime` for module
  descriptor discovery, dependency normalization, generated project selection,
  runtime registry filtering, reconnect behavior, timeout and cancellation,
  Newtonsoft serialization, and Apple/Google provider mocks.
- Added an opt-in IL2CPP player build smoke test enabled with
  `PLAYSERV_RUN_IL2CPP_TESTS=1`.
- Added `PlayServExternalIdentityProof` as an explicitly unverified Apple/Google
  credential handoff for the future PlayServ authentication backend.
- Added assembly-owned runtime module and local execution registration discovered
  without generated package registries.
- Added the project-owned `Playserv.Project.Generated` assembly under
  `Assets/PlayServ/Generated/Runtime` for the selected runtime module set.
- Added project-scoped module configuration in
  `ProjectSettings/PlayServModules.json`, including SDK profile, runtime modules,
  editor tools, and per-build-target overrides.
- Added automatic module graph synchronization when the project settings file
  changes or Unity switches build target.

### Changed

- Replaced fire-and-forget `async void` spawn timeout and keepalive reply
  handlers with tracked tasks that are cancelled with their module or
  connection lifetime.
- Reorganized the package to Unity's recommended UPM layout: importable examples
  now live under `Samples~/PlayServSDK`, long-form guides under
  `Documentation~`, and the root README is intentionally concise.
- Declared the PlayServ examples through the documented `samples` package
  manifest property and removed the npm-specific `files` allowlist.
- Moved the former always-compiled `Examples` assembly into the optional sample,
  so installing the SDK no longer compiles example code.
- Runtime module and editor-tool selection now migrates from `EditorPrefs` to
  version-controlled project settings. Module selection no longer depends on
  machine-local preferences.
- Optional module asmdefs are excluded by their disable define constraints,
  while `Playserv.Runtime.asmdef` and all other package source remain immutable.
- Declared the built-in IMGUI and Unity Web Request modules required by editor,
  sample, and runtime HTTP code.
- Included `package.json.meta` in registry package contents so Unity can import
  read-only package installations without attempting to modify the package.
- `Playserv.Wrapper.PlayServ` now exposes only core connection and runtime
  operations; optional features use their module-specific API surfaces.
- Module validation and repair now include the project module settings schema,
  profiles, module IDs, platform overrides, and assembly registrations.

### Fixed

- Added an idempotent upgrade cleanup for legacy package-generated compatibility
  files and runtime assembly references that an older SDK editor process could
  write into the immutable UPM cache while upgrading to the modular SDK.

### Removed

- Removed macOS `.DS_Store` files from the package working tree.
- Removed project-specific compatibility, module registry, and manifest
  generation from the SDK package directory.
- Removed the legacy `PlayServ.*` optional-module forwarders, compatibility
  registry, assembly attributes, and facade contracts.

### Security

- Removed Apple Team ID, Services ID, Key ID, redirect URI, and private key from
  client-side `PlayServAppleSignInSettings`.
- Added an editor migration that re-serializes legacy Apple settings assets so
  removed server credential fields do not remain in project YAML.
- Documented that provider profile data and tokens must not create a trusted
  PlayServ session before backend verification.

## [0.1.0] - 2026-03-04

### Added

- Initial `com.playserv.sdk` package structure with Runtime, Editor, and Samples content.
- Deployment tooling, RPC analyzer integration, and version synchronization editor workflows.
