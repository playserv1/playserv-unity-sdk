# Changelog

All notable changes to this package will be documented in this file.

The format is based on Keep a Changelog, and this project follows Semantic Versioning.

## [Unreleased]

- Expanded the Unity CI project to install all companion packages, enable every
  runtime module, and fail when any discovered test assembly is silently omitted.
- Aligned publishing with the `unity-*` source-tag convention, retained legacy
  tag compatibility, and made the GameCI coverage patch portable across hosts.
- Removed a monorepo-only generator command from the manifest header shipped to
  studios while preserving the descriptor semantic hash.
- Fixed the bundled Schema Tool runtime contract for Unity 2021.3 Linux, whose
  editor image provides .NET 5 rather than .NET 6.
- Rebuilt the bundled Schema Tool with portable Roslyn assemblies instead of
  platform-specific ReadyToRun binaries, fixing schema generation in Linux CI.
- Preserved the Schema Tool dependency manifest so Unity 2021 Linux loads the
  shipped Roslyn dependency versions during schema analysis.
- Prevented automatic SDK cache maintenance from recompiling generated module
  assemblies after a batch-mode Unity test run has already started.

## [0.4.0] - 2026-08-20

- Extended `com.playserv.debug-terminal` with an isolated Editor/Dedicated
  Server command assembly covering room lifecycle, server realtime and Records,
  acting-player writes, JWT validation, admission, Code, Analytics and
  Commerce, with secure memory-only handling for all server credentials.
- Preserved the Debug Terminal dedicated-server command bootstrap in IL2CPP
  builds through an explicit Unity linker declaration.
- Added dedicated-server Analytics with bounded retry-safe batching and
  per-event player attribution, plus server-key Catalog and Storefront facades;
  graceful shutdown now reports analytics flush failures alongside room closes.
- Exposed the runtime data table catalogue with cached list/lookup/refresh APIs,
  full metadata and ACL capabilities for both player and dedicated-server
  facades, sharing the exact cache used by typed Records resolution.
- Unified player matchmaking HTTP, network, timeout, and response failures
  under `PlayServMatchmakingException`, preserving operation/function context
  and backend codes; room-entry refusal keeps its legacy code and adds a bridge.
- Added dedicated-server player JWT validation against cached public JWKS,
  enforcing RS256 signatures, issuer, lifetime and project/environment scope
  while exposing only safe runtime claims and leaving revocation authoritative.
- Added opt-in dedicated-server realtime Records through an independent
  rotating-`sk_*` WebSocket session with the existing subscription lifecycle,
  refresh, refcounting, reconnect replay, and shutdown cleanup.
- Fixed event subscription acknowledgements to match the shipped backend
  `EventType`/`success` protocol, including out-of-order topic correlation,
  local refcounting, reconnect replay, and typed per-topic rejection errors.
- Added acting-player contexts to `com.playserv.game-server`, allowing
  player-owned Records writes through the existing validated
  `X-Acting-Player` contract without changing server read scope.
- Added credential-free `PlayServStatus` access to current platform health,
  daily PoP history, and validated federation origins using the existing
  public `/status` endpoints.
- Added client-side Records bulk/convenience APIs for cursor-based LoadAll,
  ordered LoadMany/PopulateMany, DeleteById, bounded BulkSave/BulkDelete, and
  explicitly confirmed DeleteAll without requiring backend bulk endpoints.
- Extended `com.playserv.game-server` with rotating-server-key typed Records
  and Cloud Functions access over the existing runtime endpoints.

- Added exact-byte Cloud Function requests and responses, bounded buffered
  invocation, streaming file downloads with safe replacement, upload/download
  progress, and a backward-compatible optional binary HTTP transport capability.
- Added realtime-only typed OR groups and nested relation includes. Common
  `Where` clauses are ANDed into every alternative, values remain dataflow
  variables, and REST rejects OR or nested expansion before HTTP I/O.
- Added cancellable public realtime refresh through
  `IPlayServRefreshableSubscription` for collection, typed-record, and legacy
  shared-entity handles. Refresh uses the current reconnect-remapped server ID;
  typed records wait for canonical value, timestamp, snapshot, and ETag sync.
- Added the optional `com.playserv.game-server` Unity Dedicated Server package
  with per-request `sk_*` resolution, server matchmaking and launch, room
  lifecycle, reservation admission, safe player lookup, independent multi-room
  heartbeats, placement acknowledgments, and graceful shutdown.
- Aligned the core SDK, all companion packages, and Schema Tool release
  metadata with `0.4.0`.

## [0.3.9] - 2026-08-19

- Expanded Debug Terminal coverage for Analytics, Cloud Functions,
  Catalog/Storefront, complete player matchmaking, Data ACL capabilities, and
  realtime record handles, with strict JSON validation, cancellation, bounded
  function previews, and reservation-token redaction.
- Added optional Steamworks.NET, EOS-Contrib, and Meta Unity SDK auth companion
  packages. Each reflection bridge acquires a provider credential without a
  hard third-party dependency and exposes one-step PlayServ login/link helpers;
  tokens remain memory-only and IL2CPP preservation is included.
- Added `PlayServRecord<T>.SubscribeAsync()` with canonical realtime refresh of
  value, snapshot, timestamps, and ETag; top-level changed-field notifications;
  explicit backend-wins conflict reporting for pending local edits; reconnect
  replay; and terminal `IsDeleted` handling after server-side deletion.
- Added runtime Data ACL capabilities from the existing table catalogue,
  including client/server/backend read-write flags, advisory Records and typed
  realtime prechecks, refreshable snapshots, and typed local/backend access
  denial errors. Missing ACL metadata remains backward-compatible as unknown.
- Added read-only `PlayServCatalog` and `PlayServStorefronts` runtime HTTP
  facades with typed catalog mappings, bundles, storefront audiences,
  schedules and statistics, plus filtering and bidirectional cursor metadata.
- Added `PlayServCode` for typed and raw calls through the existing
  `/fn/{slug}` cloud-function gateway, including all supported HTTP verbs,
  query values, tagged versions, safe pass-through headers, response metadata,
  per-request timeouts, optional player authorization, and unified errors.
- Changed the optional Analytics module's default delivery from the unused
  `module_analytics.TrackAnalyticsBatch` WebSocket command to the shipped
  `POST /analytics/events` runtime endpoint, preserving batching, player
  attribution, custom providers, and queued retry behavior.
- Added the complete player-safe matchmaking surface: backward-compatible
  request-based Find/Join overloads with typed `params` lobby state, configurable
  long-poll waits, SDK-managed search age, and player-authenticated
  `LaunchServerAsync`. HTTP deadlines come from the same `wait_ms` sent to the
  server plus a five-second network margin.
- Added automatic Android/iOS DeviceBan fingerprints for anonymous and provider
  login. The SDK sends only an application-scoped SHA-256 `device_id_hash`,
  preserves custom-provider precedence and never uses the signal for session
  recovery; unsupported platforms continue without it with a development warning.
- Added the optional `com.playserv.debug-terminal` companion package with an
  authenticated diagnostics scene assembled from reusable connection and
  terminal prefabs; removed the old terminal hub from the core sample.
- Expanded Debug Terminal to cover the `0.3.8` core SDK: module/session
  diagnostics, identity lifecycle, Typed Records V2, managed subscriptions,
  awaitable RPC, raw events, spawn scopes, keepalive counters, and unified
  errors. Provider credentials are collected only through a secure in-memory
  modal and never enter command history.
- Fixed WebGL builds without the optional WebRTC companion by moving the
  `Ws_Connect`, `Ws_Send`, and `Ws_Close` JavaScript bridge into the core
  package. The WebRTC plugin now exports only its signaling and RTC symbols,
  avoiding duplicate exports when both transports are installed.

## [0.3.8] - 2026-08-17

- Added player-auth provider discovery and managed identity link, unlink, and
  conflict-driven merge over the existing runtime auth endpoints.
- Added typed merge/provider conflicts, identity mutation certainty, linked
  provider claims, and safe transport recovery for session-changing operations.
- Added opt-in validated player fingerprint providers for anonymous creation and
  provider recovery; the SDK performs no automatic device fingerprinting.
- Added native proof factories for Facebook Limited Login, Epic, Steam, and
  PlayServ tokens alongside Apple and Google ID tokens.
- Added the cross-module `PlayServError` model with stable categories, original
  source codes, HTTP/transport metadata, retryability, and backward-compatible
  bridges from transport, auth, records, subscription, and RPC errors.
- Added refcounted realtime handles, deterministic `CloseAsync`, best-effort
  close from `Dispose`, reconnect replay with server-ID rebinding, and terminal
  completion after server cancellation or record deletion.
- Added typed V2 records through `PlayServData.Records<T>()`, including
  server-minted IDs, managed record/singleton handles, create/load/query,
  load-or-create, merge-patch save, reload, and delete.
- Added selector-based filters and sorting with `PlayServField`,
  `PlayServJsonName`, and `PlayServIgnore` wire-name parity, plus projection,
  expansion, hidden columns, search, and bidirectional cursor metadata.
- Added a shared typed query grammar for REST and realtime collections,
  expression-based AND/comparison/In/null filters, positive field projection,
  direct relation includes, and `Records<T>().SubscribeAsync(...)`.
- Added explicit realtime capability errors for query features unsupported by
  the existing dataflow transport; legacy keyed `Where` can no longer be
  silently ignored, while keyed `Select` and `Include` now affect the request.
- Added ETag tracking and typed not-found/conflict exceptions;
  partial handles must be fully reloaded before saving.
- Added natural-key lookup over the existing records query endpoint. Verified
  `LoadOrCreateAsync` never returns a server-minted ID that cannot be reloaded
  and recovers a concurrent winner with a second query.
- Added the `PlayServAuth` facade for external provider login, explicit logout,
  managed session state, typed conflicts, structured failures, and
  `SessionLost` notifications.
- Generalized anonymous authentication into managed anonymous and registered
  player sessions with serialized login/logout/refresh operations, rotated
  refresh-token persistence, refresh expiry, and legacy PlayerPrefs migration.
- Added provider login and sign-out runtime HTTP calls, optional player bearer
  authorization, and structured parsing for Problem Details and `409`
  provider-account conflicts.
- Added controlled WebSocket reconnects after login/logout and in-place live
  authorization updates after ordinary access-token refresh.
- Added optional transport close-reason reporting for the built-in desktop and
  WebGL WebSocket transports and mapped terminal backend reasons to
  `PlayServAuth.SessionLost`.
- Updated Apple and Google proof helpers to submit ID tokens only; credentials
  containing only an authorization code are no longer presented as ID tokens.
- Aligned the core SDK, companion packages, and Schema Tool version metadata
  with `0.3.8`.

## [0.3.7] - 2026-08-12

- Added built-in anonymous player-session management. With a public client token
  and no explicit runtime credential provider, `PlayServ.Connect()` now creates
  or restores the player, supplies its JWT to the handshake, and rotates it for
  live connections and reconnects.
- Added runtime-only `IPlayServPlayerSessionStore` customization and a default
  PlayerPrefs-backed store for refresh credentials and player IDs.
- Added `EnableAutomaticPlayerAuthentication` for clients that need to retain
  legacy client-token-only or externally managed authorization behavior.
- Aligned the core SDK, companion packages, and Schema Tool version metadata
  with `0.3.7`.

## [0.3.6] - 2026-08-12

- Aligned the core SDK, companion packages, runtime handshake, and Schema Tool
  version metadata with `0.3.6`.
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
  committed `PlayServBuiltInModuleManifest.g.cs`. Automated drift verification
  was not included in 0.3.5; it was added later in the platform source repository.
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
