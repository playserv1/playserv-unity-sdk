# Changelog

All notable changes to this package will be documented in this file.

The format is based on Keep a Changelog, and this project follows Semantic Versioning.

## [Unreleased]

## [0.3.2] - 2026-07-25

- Added the optional `com.playserv.schema-tool` companion package, delivered by
  UPM and executed as an external process through Unity's bundled .NET runtime.
- Added Roslyn-based `[PlayServSchema]` contract discovery, deterministic JSON
  Schema 2020-12 output, optional backend C# DTO generation, source/output
  hashes, and `playserv.schema.lock.json`.
- Added `init`, `status`, `analyze`, `generate`, `validate`, `sync`, `watch`,
  and `doctor` CLI commands plus a project-local Rider/IDE launcher.
- Added Schema Tool install/remove, analysis, generation, validation, and
  watcher controls to the main SDK window.
- Added an optional provider-based Analytics module with typed event parameters,
  explicit user properties, PlayServ user/session/app context, bounded
  in-memory queueing, batched delivery, reconnect flushing, and collection
  consent controls.
- Added the `PlayServAnalytics` facade, replaceable
  `IPlayServAnalyticsProvider`, and the default
  `module_analytics.TrackAnalyticsBatch` transport contract.
- Added Analytics runtime tests for typed batches, queue overflow, failed-send
  retention, collection disabling, custom providers, and unsupported values.
- Documented migration from the Firebase-backed eggie-crush analytics wrapper
  and the remaining PlayServ backend ingestion requirements.
- Split Apple Sign In, Google Sign In, and WebRTC into standalone companion UPM
  packages under `CompanionPackages~`; the core package no longer imports their
  runtime, editor, test, native, or WebGL plugin assets.
- Added Git subfolder installation instructions for
  `com.playserv.apple-signin`, `com.playserv.google-signin`, and
  `com.playserv.webrtc`.
- Added companion package installation and removal controls to the main SDK
  module settings window.
- Added separate `Installed` and `Enabled` companion states, a committed
  generated package catalog, and UPM-backed module lifecycle removal.
- Added versioned PlayServ cache maintenance that removes stale generated state,
  codegen cache, and inactive PlayServ package caches after SDK updates.
- Added confirmed full `Library` rebuild and restart tooling for corrupted Unity
  project caches.

## [0.3.1] - 2026-07-24

- Removed serialized runtime authorization and deployment tokens from
  `PlayServConfig`; player JWTs now come from
  `IPlayServRuntimeTokenProvider`, while deploy credentials use
  `PLAYSERV_DEPLOY_AUTH_TOKEN` or project-scoped local Editor storage.
- Added runtime credential validation that accepts only public `pk_*` client
  tokens and rejects `Bearer sk_*` before player connections.
### Added

- Added typed awaitable RPC through
  `PlayServRpc.InvokeAsync<TRequest, TResponse>`, including client request IDs,
  configurable timeout, cancellation, structured errors, typed JSON response
  deserialization, and compatibility correlation for older gateways.
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

- Moved the source-only SDK version synchronization command and asset
  postprocessor into the development repository, so release tooling is no
  longer shipped in UPM or `.unitypackage` artifacts.
- Made built-in `module.playserv.json` descriptors the single source of truth
  and replaced the hand-maintained runtime fallback with a deterministic,
  committed `PlayServBuiltInModuleManifest.g.cs` plus CI drift verification.
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
