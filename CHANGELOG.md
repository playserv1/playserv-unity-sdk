# Changelog

All notable changes to this package will be documented in this file.

The format is based on Keep a Changelog, and this project follows Semantic Versioning.

## [Unreleased]

- Marked every runtime-registration assembly with `AlwaysLinkAssembly` so UnityLinker
  keeps dynamically discovered PlayServ modules, including WebSocket transport, in
  IL2CPP player builds even when game code has no direct reference to their types.
- Added anonymous player sign-in and refresh endpoints to the runtime HTTP client,
  including player token response contracts and WebSocket-to-HTTP endpoint mapping.
- Added awaitable live player-auth rotation through `RefreshAuthRequest`, with
  timeout, cancellation, serialized refresh calls, and reconnect credential reuse.
- Added `PlayServ.RefreshPlayerAuthAsync` and
  `PlayServConnection.RefreshPlayerAuthAsync`; initial player JWTs remain
  runtime-only and are never serialized into `PlayServConfig`.
- Made backend, deploy API, schema API, and dashboard addresses editable in
  `PlayServConfig`; project values now override baked package defaults.
- Changed environment selection to explicitly apply its endpoint defaults to
  the config while preserving subsequent project-specific edits.
- Made client-defined analytics providers configurable before PlayServ
  connection setup and persistent across runtime reconnects.
- Added `PlayServAnalytics.HasCustomProvider` and `ResetProvider()` while
  keeping Firebase and other vendor adapters in client projects.
- Restored server schema download and C# model generation in the main SDK
  window alongside the external local-contract Schema Tool.
- Split the schema UI into explicit `C# -> local outputs` and
  `Schema API -> JSON Schema -> Unity C#` workflows, with advanced tooling
  hidden behind a separate control.
- Added downloaded/current schema comparison by content hash, version,
  timestamp, and definition count before applying server changes.
- Changed server schema generation to validate the complete schema and all
  generated file names before touching project output, preventing data loss on
  malformed schemas and rejecting generated path traversal.

## [0.3.4] - 2026-07-25

- Extracted Analytics, Pulse, UDP, and RUDP from the core SDK into optional
  companion UPM packages.
- Added `com.playserv.analytics`, `com.playserv.pulse`, and
  `com.playserv.transports-native`; the native transport package owns
  independently configurable UDP and RUDP modules.
- Extended the companion package catalog, settings UI, and removal lifecycle to
  support multiple module IDs in one package.
- Kept companion packages visible and installable from the main PlayServ SDK
  module settings even when their code is not installed.
- Reduced the Client SDK profile to core gameplay modules plus WebSocket.
  Installed companion modules are enabled by the Full SDK profile or explicitly
  by the project.
- Changed the placeholder Pulse module to disabled by default.
- Updated all PlayServ packages and the external Schema Tool to `0.3.4`.
- Added explicit Schema Tool runtime discovery coverage for Unity 2021.3
  through Unity 6.6 layouts and a build-time Roslyn override for producing the
  shared .NET 6-compatible tool assembly with newer SDKs.

## [0.3.3] - 2026-07-25

- Added the optional `com.playserv.schema-tool` companion package, delivered by
  UPM and executed as an external process through Unity's bundled .NET runtime.
- Added Roslyn-based `[PlayServSchema]` contract discovery, deterministic JSON
  Schema 2020-12 output, optional backend C# DTO generation, source/output
  hashes, and `playserv.schema.lock.json`.
- Added `init`, `status`, `analyze`, `generate`, `validate`, `sync`, `watch`,
  and `doctor` CLI commands plus a project-local Rider/IDE launcher.
- Added Schema Tool install/remove, analysis, generation, validation, and
  watcher controls to the main SDK window.

## [0.3.2] - 2026-07-25

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
