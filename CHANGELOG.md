# Changelog

All notable changes to this package will be documented in this file.

The format is based on Keep a Changelog, and this project follows Semantic Versioning.

## [Unreleased]

## [0.6.9] - 2026-09-24

Release prepared locally; distribution tags and registry publication are separate.

### Fixed

- Preserve the UTC or explicit offset of server-image credential expiration dates.
  Fresh credentials are no longer rejected because of the Editor's local time zone
  or regional date format, including Ukrainian and Russian locales.
- Distinguish malformed registry credentials from expired credentials before
  Docker login or push, without exposing secrets in error messages.

## [0.6.8] - 2026-09-24

Release prepared locally; distribution tags and registry publication are separate.

### Added

- Register a C# multi-room game server directly from Deployment → Server Images,
  then refresh and select it for image building. Registration preserves the
  connected project/environment and never starts a machine or changes a pool.
- Handle conflicting slugs, cancellation and uncertain creation responses without
  automatically repeating the write. Keep the registration form stable across
  IMGUI Layout/Repaint passes.

### Changed

- Show Server Token as visible text below Client Token in Settings and Inspector.
  Edits save locally immediately; emptying the field clears the token. Remove the
  Save locally and Clear buttons. Process-provided tokens remain read-only, and
  server tokens remain excluded from assets and player builds.

## [0.6.7] - 2026-09-24

Release prepared locally; distribution tags and registry publication are separate.

### Fixed

- Keep Deployment layout stable while Docker logs and asynchronous connection,
  build, publication and function-preview results arrive. Server Images and
  Platform Functions render one captured view per Layout/Repaint cycle.
- Revalidate the current Deployment target before acting on a displayed result;
  cancellation and late callbacks cannot reactivate an obsolete operation.

## [0.6.6] - 2026-09-24

Release prepared locally; distribution tags and registry publication are separate.

### Changed

- Deployment now opens Platform Functions first, followed by Server Images and RPC.
  Platform Functions and Server Images share Dashboard Address from the current config.
- Move Server Token below Client Token in Settings and Inspector, with separate local
  storage per Unity project, config and Dev/Prod environment. `PLAYSERV_API_KEY` remains
  read-only and takes precedence; server tokens are excluded from player builds.
- Use `.com` Dev/Prod dashboard defaults and migrate known old `.io` defaults while
  preserving custom addresses. Migrate the old local Deployment token once only when
  its environment is known; otherwise preserve it and request manual re-entry.

### Fixed

- Find Docker Desktop from Unity Hub's minimal macOS PATH and resolve credential helpers
  in the child process without changing the Editor environment. Distinguish missing CLI,
  launch failure, unavailable daemon and non-Linux daemon diagnostics.
- Reject operator sessions for the wrong selected environment. Changes to config,
  address, environment or token cancel local operations, discard stale responses,
  clear prepared images and require reconnecting.

## [0.6.5] - 2026-09-23

Release prepared locally; distribution tags and registry publication are separate.

### Added

- Add Editor Deployment → Server Images: build and inspect linux/amd64 images,
  select an existing game server, publish with one-hour project credentials,
  verify the manifest digest, and check uncertain publications without retrying
  a push. Operations support cancellation, bounded logs and isolated Docker auth.

### Fixed

- Run Platform Functions async HTTP test scenarios through synchronous NUnit entry
  points, so Unity 2021.3 executes them instead of rejecting their Task return type.
- Keep archive-test fixtures in the project's Temp directory so macOS's `/var`
  symlink does not trigger the packager's source-path safety check.

## [0.6.4] - 2026-09-20

Release prepared locally; distribution tags and registry publication are separate.

### Added

- Add a separate Platform Functions mode to Editor Deployment for C# cloud functions
  and game servers: local operator credentials, source preview, portable tar.gz upload,
  terminal-status polling and deployment-status recovery. Existing RPC deployment and
  game CI remain available.

### Fixed

- Keep Platform Functions exception assertions compatible with Unity 2021.3's
  NUnit package; wait for asynchronous failures without blocking the Unity context.

## [0.6.3] - 2026-09-18

Release prepared locally; distribution tags and registry publication are separate.

### Added

- Add typed record references with ID-only Records serialization, expanded previews, canonical loads, scoped identity checks, shared concurrent loads and batch reads. Schema Tool recognizes typed relation targets and cardinality.

- Add opt-in WebSocket room sessions with mandatory game-owned admission/readiness, direct Host reservation handoff, bounded fresh-ticket recovery, cancellation and protocol-aware Leave. Includes a small admission/snapshot protocol example.

- Explicit browser sign-in for WebGL, Editor and desktop through platform-completion OAuth with PKCE S256, bounded claim polling, popup handling, cancellation and explicit player-switch permission. Existing sessions stay active while waiting; ambiguous claim loss is reported without an automatic retry.

### Fixed

- Keep SDK Version actions beside the version as compact icon buttons. Status stays on one line with its full text in a tooltip, so update checks and errors do not stretch the overview card.

## [0.6.2] - 2026-09-18

Release prepared locally; distribution tags and registry publication are separate.

### Fixed

- 2026-09-18: Isolate WebGL WebSocket instances and connection attempts. Parallel
  platform/game sockets no longer replace each other; send, close, reset and
  timeout are connection-local. Disposal completes pending connects and releases
  bridge objects, browser handlers and registry entries without accepting stale
  callbacks. The single-session facade and 1 MiB text-message limit are unchanged.

### Added

- 2026-09-18: Add an opt-in independent game-server WebSocket connector with the
  existing C# handshake, current-player credentials and refresh without anonymous
  fallback. Expose text messages and lifecycle on the Unity context, bounded
  connect/send, explicit WSS endpoints and safe normalized errors. HandshakeSent
  means only open plus send, not admission. No server protocol, automatic reconnect,
  ticket replay or gameplay integration is added.

- 2026-09-18: Add SDK Version update controls to PlayServ Editor: project-cached
  registry checks, explicit confirmation, and one source-preserving UPM update
  for core and installed official companions, including Schema Tool. Validate
  target compatibility and dependencies before submission, coordinate package
  operations, and verify installed results across domain reload/restart. Local
  copies and unsupported sources are protected; no automatic install, retry,
  registry changes or runtime API changes. Install the first release containing
  these controls through the ordinary Package Manager.

## [0.6.1] - 2026-09-18

Release prepared locally; distribution tags and registry publication are separate.

### Fixed

- 2026-09-18: Make interrupted-build token recovery tests deterministic across
  Editor platforms, covering busy/idle transitions and callback registration
  without a wall-clock wait. Production recovery still waits for build,
  compilation and asset import to finish.

- 2026-09-18: Keep Dev/Prod Client Tokens in independent project/config-scoped
  local Editor storage, including direct config access and one-time migration of
  the serialized key. Empty environments never reuse another environment's key.
  Player builds bake only the active public token and restore the asset, with
  interrupted-build recovery and explicit environment/token overrides for CI.
  Endpoint settings, custom providers and the runtime authentication API are unchanged.

## [0.6.0] - 2026-09-17

Release prepared locally; distribution tags and registry publication are separate.

- 2026-09-14: Opt-in typed inbound server RPC registry, authenticated caller metadata, bounded serial dispatch, cancellation and AOT-preserved sample. No implicit Records authority or automatic replay.

- 2026-09-14: Explicit runtime schema advisory for caller-visible table presence; no schema mutations or initialization gate.

### Added

- 2026-09-17 (PSV-2694): Player `HostRoomAsync` with flat requested attributes,
  authoritative matched reservation, typed hosting refusals and Retry-After.
  A configurable total budget covers authentication and HTTP; no automatic
  retry, launch, additional join or networking connection is performed.

- 2026-09-17 (PSV-2556): Game Server companion `Uplink.Data` exposes typed single-key
  queries and send-only upsert/increment/delete, with scoped catalogue checks,
  immutable send snapshots, bounded correlation and connection-specific cleanup.
  Existing Records APIs are unchanged. Mutation acknowledgements and data-subscription
  routing remain outstanding; local ACL checks do not replace backend enforcement.

- 2026-09-16: Optional game-selected display names for anonymous sign-in, external
  login and first provider link (`display_name`). Names are trimmed and capped at
  64 UTF-16 code units without splitting surrogate pairs. Provider profile names
  are never copied automatically; existing players are not renamed. Legacy custom
  HTTP modules can continue anonymous sign-in without the optional name.

- 2026-09-15 (PSV-2628): Game Server room factories receive optional requested
  attributes as independent snapshots and may apply, alter, ignore or refuse them
  with `ContentRefused`. The existing callback signature and no-attributes path
  remain compatible; no requested values are automatically merged into registration.

- 2026-09-14: Explicit provider-neutral launch room and public endpoint helpers; no networking or infrastructure discovery side effects.

- 2026-09-14: Server room configuration REST reads and ordered uplink state/configuration notifications.

- 2026-09-14: Opt-in game connection tracking with pushed-ticket reconnect grace, generation-specific rejection and single leave on expiry.

- 2026-09-14: Opt-in bounded named-room join/connection flow with Retry-After, address readiness and monotonic ticket expiry checks.

- 2026-09-14: Named-room join parameters and server-authorized direct join for an explicit player; existing join calls keep their no-body, no-retry behavior.

- 2026-09-13: Game Server pushed-ticket admission (PSV-2556 stage two), enabled
  by default. Existing consume-based games must migrate or set
  `EnablePushedAdmission = false`; see the companion migration guide.

- 2026-09-12 (PSV-2602): Server reservations expose endpoint/region/attribute
  snapshots and an optional monotonic TTL, with strict wire integer validation.

- 2026-09-12: Dedicated Server structured logs with pre-enqueue credential
  redaction, bounded memory and best-effort shutdown flush reporting.

- 2026-09-12: Dedicated Server score submission and immutable leaderboard reads,
  with bounded correlated requests and disconnect/timeout/cancellation cleanup.

- 2026-09-12: Dedicated Server event publishing over uplink with explicit group
  scope, reliable-lane selection and no automatic reconnect replay.

- 2026-09-12: Dedicated Server online player-session verification, including
  revocation/status verdicts and credential-safe diagnostics.

- Dedicated Server companion: stage-one dial-in uplink, session-authorized REST,
  platform-owned room configuration and explicit non-bot roster updates.
- Dedicated Server companion: optional platform-requested room creation factory;
  backend dispatch is still gated on PSV-2590.

### Changed

- 2026-09-17 (PSV-2694): Synchronized all thirteen packages, internal dependencies,
  runtime version reporting and the rebuilt Schema Tool to `0.6.0`.

- 2026-09-12 (PSV-2602): Client/server launch and companion room list,
  registration/heartbeat and close use the served `/rooms/...` namespace.
  Algorithmic find and reservation consume retain their routes; no fallback added.

### Fixed

- 2026-09-14: Game Server room creation now distinguishes temporary
  `room_quota_exceeded` from `instance_draining` and sends `room_create_failed` for
  factory exceptions with only a bounded type name. Quota/factory failure keeps
  the instance eligible for later requests; timeout and registration errors do
  not produce late or duplicate results.

## [0.5.1] - 2026-09-12

- Fixed first-connection RPC responses being dropped by attaching command routes
  after module initialization. Failed initialization also cleans up safely before
  a command router exists; RPC remains optional.

- Fixed Unity CI preparation after GameCI's floating `v4` tag switched runners:
  pin the compatible Node 24-based v4.3.2 action and its coverage-patch path.
  Skip result validation/upload only when the corresponding Unity test step was
  skipped; a started test run without its NUnit XML still fails validation.

## [0.5.0] - 2026-09-12

- 2026-09-12 (PSV-2557): Added player room browsing and direct room join for the
  `/rooms` contract, typed room refusals, public connect/region/attribute metadata
  and a monotonic reservation lifetime. Existing FindMatch/JoinGame calls remain
  compatible. New routes require PSV-2600; room admission-push refusals require
  PSV-2601. Verified with HTTP fixtures, not a deployed backend integration.

- Added `Records<T>().QueryViewAsync` for paginated reads through an existing
  saved View, including server/acting callers. View results remain partial until
  explicitly reloaded, protecting hidden columns from accidental writes.

- Added opt-in strict JSON response types for typed Code (client and Game Server)
  and awaitable RPC calls. Unsupported codecs/contracts fail before sending;
  mismatch diagnostics identify fields and types without copying response values.

- Fixed record and singleton saves losing local edits made while a write is in
  flight. Newer top-level changes remain pending against the acknowledged server
  snapshot/ETag; explicit reload and realtime backend-wins semantics are unchanged.

- Added optional caller-provided idempotency keys to Records `CreateAsync` and
  native `BulkCreateAsync`, including Game Server and acting-player callers, so
  games can explicitly retry an identical request after losing its response.
  Existing calls still generate a fresh key; no automatic retries are introduced.

- Mapped group subscription error 02006 to a stable, non-retryable unified
  error; rejected groups never enter the reconnect replay set.

- Added credential-free project status with deployment, counters, probe and
  latency models, preserving unavailable metrics as null.

- Added typed nested inclusion-field filters (up to eight segments) for REST and
  realtime, honoring wire-name attributes and keeping query values out of query text.

- Optimized plain LoadMany/PopulateMany reads using bounded native ID queries,
  preserving input order, independent duplicate handles and typed item failures.

- Added native natural-key upsert, atomic bulk-upsert, patch and delete APIs,
  including seed/managed modes, ETags, idempotency keys and acting-player writes.

- Fixed outbound and inbound transport frame diagnostics so public client
  tokens, server keys, bearer credentials and JWTs are redacted before any
  message reaches the configured logger, including malformed-frame and
  deserialization-error paths. Network payloads remain unchanged.
- **Breaking:** removed caller-supplied `GameId` and `UserId` from runtime
  settings, convenience configuration, WebSocket/WebRTC handshakes, and the
  dedicated-server realtime options. Runtime project and player identity now
  come exclusively from `pk_*`/`sk_*` credentials and authenticated player
  sessions. Optional `DeploymentGameId` remains Editor/tooling-only.
- Added a code-first Schema Tool push workflow that converts attributed C#
  contracts into one atomic backend schema bundle with revision preflight,
  offline dry-run validation, and environment-only server credentials.
- Enforced the backend's 1 MiB WebSocket message bound before desktop/WebGL
  sends and while assembling inbound messages, reporting close code 1009.
- Runtime event publishing now fails immediately with a typed capability error
  because the shipped backend protocol only supports event subscriptions.
- Exposed the authenticated player's safe runtime profile with cache-aware and
  forced-refresh APIs, preserving ETag while excluding operator-only fields.
- Added indexed natural-key record loads plus native atomic bulk-create and
  delete-by-filter APIs, preserving server-minted record IDs and explicit
  confirmation for match-everything deletes.

## [0.4.1] - 2026-08-23

- Documented copy-paste GitHub UPM installation URLs, immutable distribution
  tags, and deterministic upgrade/downgrade steps for core and companion packages.
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
- Synchronized core, companion-package, runtime, documentation, and Schema Tool
  versions for the `0.4.1` release.

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
