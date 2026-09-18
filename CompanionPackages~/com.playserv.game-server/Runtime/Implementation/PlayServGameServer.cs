using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Analytics;
using Playserv.Code;
using Playserv.Commerce;
using Playserv.Data;
using Playserv.Http.Interfaces;
using Playserv.Serialization;
using Playserv.Wrapper;

namespace Playserv.GameServer
{
    /// <summary>
    /// Process-wide facade for PlayServ operations that require a Unity Dedicated Server
    /// build and an <c>sk_*</c> credential.
    /// </summary>
    public static partial class PlayServGameServer
    {
        internal const int NetworkMarginMs = 5_000;
        internal const int DefaultHttpTimeoutSeconds = 10;
        private const string ServerKeyEnvironmentVariable = "PLAYSERV_SERVER_KEY";
        private const string ApiUrlEnvironmentVariable = "PLAYSERV_API_URL";

        private static readonly object Sync = new object();
        private static readonly Dictionary<string, PlayServGameRoomHandle> Rooms =
            new Dictionary<string, PlayServGameRoomHandle>(StringComparer.Ordinal);
        private static readonly HashSet<string> StartingRooms =
            new HashSet<string>(StringComparer.Ordinal);
        private static readonly PlayServPlayerTokenValidator PlayerTokenValidator =
            new PlayServPlayerTokenValidator();

        private static PlayServGameServerContext _context;
        private static bool? _supportedBuildOverrideForTesting;
        private static readonly string ProcessInstanceId = Guid.NewGuid().ToString("N");

        /// <summary>Process-owned room uplink, independent of the opt-in Records /ws connection.</summary>
        public static PlayServGameServerUplink Uplink { get; private set; } = new PlayServGameServerUplink();

        /// <summary>Server-to-client events sent over the explicitly connected uplink.</summary>
        public static PlayServGameServerEvents Events { get; } = new PlayServGameServerEvents();

        /// <summary>Legacy project-level scores and Top queries on the connected uplink.</summary>
        public static PlayServGameServerLeaderboards Leaderboards { get; } = new PlayServGameServerLeaderboards();

        /// <summary>Opt-in structured server logs. Never captures Debug.Log or Analytics automatically.</summary>
        public static PlayServGameServerLogs Logs { get; } = new PlayServGameServerLogs();

        /// <summary>Server-authorized Cloud Functions facade.</summary>
        public static PlayServGameServerCode Code { get; } = new PlayServGameServerCode();

        /// <summary>Bounded dedicated-server analytics with per-event player attribution.</summary>
        public static PlayServGameServerAnalytics Analytics { get; } =
            new PlayServGameServerAnalytics();

        /// <summary>Read-only server-authorized Catalog facade.</summary>
        public static PlayServGameServerCatalog Catalog { get; } =
            new PlayServGameServerCatalog();

        /// <summary>Read-only server-authorized Storefront facade.</summary>
        public static PlayServGameServerStorefronts Storefronts { get; } =
            new PlayServGameServerStorefronts();

        /// <summary>Opt-in, independent server-key WebSocket session for Records subscriptions.</summary>
        public static PlayServGameServerRealtime Realtime { get; } = new PlayServGameServerRealtime();

        /// <summary>Whether the facade currently has validated process configuration.</summary>
        public static bool IsConfigured
        {
            get
            {
                lock (Sync)
                    return _context != null;
            }
        }

        /// <summary>Configures or reconfigures the facade while no managed rooms are active.</summary>
        public static void Configure(PlayServGameServerOptions options)
        {
            ConfigureCore(options, null, null, null);
        }

        /// <summary>
        /// Opens typed Records using the configured rotating <c>sk_*</c> credential and
        /// resolves the table from <typeparamref name="T"/>'s CLR type name.
        /// </summary>
        public static PlayServRecordSet<T> Records<T>() => CreateRecordSet<T>(null);

        /// <summary>Opens typed server-authorized Records for an explicit <c>ent_*</c> ID.</summary>
        public static PlayServRecordSet<T> Records<T>(string entityId)
        {
            if (string.IsNullOrWhiteSpace(entityId))
                throw new ArgumentException("Entity ID is required.", nameof(entityId));
            return CreateRecordSet<T>(entityId.Trim());
        }

        /// <summary>Explicit server-authorized table presence advisory. NotVisible may mean an ACL restriction.</summary>
        public static Task<IReadOnlyList<PlayServSchemaAdvisory>> CheckSchemaAsync(
            IEnumerable<Type> entityTypes, CancellationToken ct = default)
        {
            EnsureSupportedBuild();
            return CreateRecordsClient().CheckSchemaAsync(entityTypes, ct);
        }

        /// <summary>Returns the cached server-visible runtime table catalogue.</summary>
        public static Task<IReadOnlyList<PlayServDataTableInfo>> GetTablesAsync(
            CancellationToken cancellationToken = default)
        {
            EnsureSupportedBuild();
            return CreateRecordsClient().GetTablesAsync(false, cancellationToken);
        }

        /// <summary>Forces an atomic refresh of the server-visible table catalogue.</summary>
        public static Task<IReadOnlyList<PlayServDataTableInfo>> RefreshTablesAsync(
            CancellationToken cancellationToken = default)
        {
            EnsureSupportedBuild();
            return CreateRecordsClient().GetTablesAsync(true, cancellationToken);
        }

        /// <summary>Finds one server-visible table by <c>ent_*</c> ID or schema name.</summary>
        public static Task<PlayServDataTableInfo> GetTableAsync(
            string idOrName,
            CancellationToken cancellationToken = default)
        {
            EnsureSupportedBuild();
            return CreateRecordsClient().GetTableAsync(idOrName, cancellationToken);
        }

        /// <summary>
        /// Creates a memory-only acting-player context. Mutating Records requests carry
        /// the supplied player JWT in <c>X-Acting-Player</c>; server reads keep owner bypass.
        /// </summary>
        public static PlayServGameServerPlayerContext AsPlayer(string playerJwt)
        {
            EnsureSupportedBuild();
            return new PlayServGameServerPlayerContext(ValidatePlayerJwt(playerJwt));
        }

        /// <summary>
        /// Validates a player JWT locally against PlayServ JWKS and the expected project/environment.
        /// This does not check server-side revocation; reservation admission remains authoritative.
        /// </summary>
        public static Task<PlayServPlayerTokenValidationResult> ValidatePlayerTokenAsync(
            string jwt,
            PlayServPlayerTokenValidationOptions options,
            CancellationToken cancellationToken = default)
        {
            EnsureSupportedBuild();
            cancellationToken.ThrowIfCancellationRequested();
            var context = GetContext();
            return PlayerTokenValidator.ValidateAsync(
                context.BackendServerAddress,
                context.HttpTimeout,
                jwt,
                options,
                cancellationToken);
        }

        /// <summary>Registers a room, returns its managed handle, and starts heartbeats.</summary>
        public static async Task<PlayServGameRoomHandle> StartRoomAsync(
            PlayServStartRoomRequest request,
            CancellationToken cancellationToken = default)
        {
            EnsureSupportedBuild();
            cancellationToken.ThrowIfCancellationRequested();
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (request.Snapshot == null)
                throw new ArgumentException("A room snapshot is required.", nameof(request));

            var context = GetContext();
            var slug = ValidateFunctionSlug(request.FunctionSlug);
            if (context.ShuttingDown) throw UplinkErrors.Exception("instance_draining");
            ValidateSnapshot(request.Snapshot, nameof(request));
            if (context.EnableUplink) await Uplink.ConnectForRoomAsync(slug, cancellationToken);
            var config = context.EnableUplink ? Uplink.RoomConfiguration : null;
            if (context.EnableUplink && config == null) throw UplinkErrors.Exception("room_configuration_missing");
            var initialSnapshot = config == null ? request.Snapshot : request.Snapshot.WithCapacity(config.Capacity);
            var key = BuildRoomKey(slug, request.Snapshot.RoomName.Trim());
            lock (Sync)
            {
                if (Rooms.ContainsKey(key) || StartingRooms.Contains(key))
                {
                    throw new InvalidOperationException(
                        "An active PlayServ room handle already exists for this function and room name.");
                }

                StartingRooms.Add(key);
                if (config != null && Rooms.Count + StartingRooms.Count > config.MaxRooms)
                {
                    StartingRooms.Remove(key);
                    throw UplinkErrors.Exception("room_quota_exceeded");
                }
            }

            try
            {
                return await RegisterPreparedRoomAsync(context, slug, initialSnapshot, cancellationToken);
            }
            finally
            {
                lock (Sync)
                    StartingRooms.Remove(key);
            }
        }

        internal static async Task<PlayServGameRoomHandle> RegisterPreparedRoomAsync(PlayServGameServerContext context,
            string slug, PlayServGameRoomSnapshot snapshot, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (context.ShuttingDown) throw UplinkErrors.Exception("instance_draining");
            Uplink.PrepareAdmissionRoom(snapshot.RoomName);
            try
            {
                var initial = await UpsertRoomCoreAsync(context, slug, snapshot, ct);
                ct.ThrowIfCancellationRequested();
                var key = BuildRoomKey(slug, snapshot.RoomName.Trim());
                PlayServGameRoomHandle handle;
                lock (Sync)
                {
                    if (!ReferenceEquals(_context, context) || context.ShuttingDown)
                        throw UplinkErrors.Exception("room_registration_outcome_unknown");
                    handle = new PlayServGameRoomHandle(context, slug, snapshot, key, RemoveRoom);
                    Rooms.Add(key, handle);
                }
                handle.ApplyConfiguration(Uplink.RoomConfiguration);
                Uplink.AttachAdmissionRoom(snapshot.RoomName, handle);
                handle.ApplyInitialHeartbeat(initial, context.UtcNow());
                handle.StartHeartbeatLoop();
                return handle;
            }
            catch { Uplink.ClearAdmissionRoom(snapshot.RoomName); throw; }
        }

        internal static bool TryReserveRequestedRoom(PlayServGameServerContext context, string slug,
            string name, int maxRooms, out string refusal)
        {
            lock (Sync)
            {
                var key = BuildRoomKey(slug, name);
                if (Rooms.ContainsKey(key) || StartingRooms.Contains(key)) refusal = "room_name_conflict";
                else if (!ReferenceEquals(_context, context) || context.ShuttingDown)
                    refusal = "instance_draining";
                else if (Rooms.Count + StartingRooms.Count >= maxRooms) refusal = "room_quota_exceeded";
                else { StartingRooms.Add(key); refusal = null; return true; }
                return false;
            }
        }
        internal static void ReleaseRequestedRoom(PlayServGameServerContext context, string slug, string name)
        {
            lock (Sync) { if (ReferenceEquals(_context, context)) StartingRooms.Remove(BuildRoomKey(slug, name)); }
        }

        /// <summary>Runs server-authorized matchmaking for an explicit player ID, including endpoint metadata and an advisory monotonic reservation lifetime.</summary>
        public static async Task<PlayServServerMatchResult> FindMatchForPlayerAsync(
            PlayServServerFindMatchRequest request,
            CancellationToken cancellationToken = default)
        {
            EnsureSupportedBuild();
            cancellationToken.ThrowIfCancellationRequested();
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            var context = GetContext();
            var slug = ValidateFunctionSlug(request.FunctionSlug);
            var playerId = ValidatePlayerId(request.PlayerId, nameof(request));
            if (request.WaitMs < 0 || request.WaitMs > 25_000)
                throw new ArgumentOutOfRangeException(nameof(request), "WaitMs must be between 0 and 25000.");
            if (request.SearchAgeMs < 0)
                throw new ArgumentOutOfRangeException(nameof(request), "SearchAgeMs must be non-negative.");
            var parametersJson = PlayServGameServerJson.SerializeOptionalObject(request.Parameters, nameof(request));
            var body = PlayServGameServerJson.Serialize(new FindMatchRequestWire
            {
                player_id = playerId,
                matchmaker = NormalizeOptional(request.Matchmaker),
                parameters = PlayServGameServerJson.ParseOptionalObject(parametersJson),
                wait_ms = request.WaitMs,
                search_age_ms = request.SearchAgeMs
            });
            var response = await SendAsync(
                context,
                "POST",
                "matchmaking/" + Escape(slug) + "/find",
                body,
                ResolveFindMatchTimeout(request.WaitMs),
                "server matchmaking",
                includeRawDetails: true,
                cancellationToken);
            return ReadServerReservation(context, response, false);
        }

        /// <summary>Requests a named-room reservation for an explicit player using server credentials. No find, launch or retry.</summary>
        public static async Task<PlayServServerMatchResult> JoinRoomForPlayerAsync(
            string functionSlug, string roomName, string playerId, object parameters = null,
            CancellationToken ct = default)
        {
            EnsureSupportedBuild();
            ct.ThrowIfCancellationRequested();
            var context = GetContext();
            var slug = ValidateFunctionSlug(functionSlug);
            var name = ValidateRoomName(roomName, nameof(roomName));
            var player = ValidatePlayerId(playerId, nameof(playerId));
            var snapshot = PlayServGameServerJson.SerializeOptionalObject(parameters, nameof(parameters));
            var body = snapshot == null
                ? PlayServGameServerJson.Serialize(new { player_id = player })
                : PlayServGameServerJson.Serialize(new { player_id = player, @params = PlayServGameServerJson.ParseOptionalObject(snapshot) });
            var response = await SendAsync(context, "POST", "rooms/" + Escape(slug) + "/" + Escape(name) + ":join",
                body, context.HttpTimeout, "server room join", includeRawDetails: false, ct);
            return ReadServerReservation(context, response, true);
        }

        private static PlayServServerMatchResult ReadServerReservation(
            PlayServGameServerContext context, PlayServGameServerHttpResponse response, bool requireMatched)
        {
            var monotonicSeconds = context.MonotonicSeconds;
            var receivedAt = monotonicSeconds();
            // The wire TTL is an integer, never Newtonsoft's coerced string or rounded fraction.
            var plain = GameServerServiceValues.Object(response.Body);
            if (plain != null)
            {
                foreach (var field in plain)
                {
                    if (!string.Equals(field.Key, "expires_in", StringComparison.OrdinalIgnoreCase) || field.Value == null)
                        continue;
                    var seconds = field.Value is long number ? number : field.Value is int integer ? integer : -1L;
                    if (seconds < 0 || seconds > int.MaxValue)
                        throw InvalidResponse("server matchmaking");
                }
            }
            var wire = DeserializeRequired<FindMatchResponseWire>(response.Body, "server matchmaking");
            var status = NormalizeOptional(wire.status) ?? "matched";
            if (requireMatched && (!string.Equals(wire.status, "matched", StringComparison.OrdinalIgnoreCase) || !wire.expires_in.HasValue))
                throw InvalidResponse("server room join");
            if (string.Equals(status, "matched", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(wire.room_name) ||
                    string.IsNullOrWhiteSpace(wire.reservation_token) ||
                    !wire.expires_at.HasValue)
                {
                    throw InvalidResponse("server matchmaking");
                }

                try
                {
                    return new PlayServServerMatchResult(
                        "matched",
                        new PlayServServerReservation(
                            wire.room_name, wire.reservation_token, wire.expires_at.Value,
                            wire.connect?.ToModel(), wire.region, wire.attributes, wire.expires_in,
                            monotonicSeconds, receivedAt),
                        wire.retry_after_ms);
                }
                catch (Exception)
                {
                    // Response conversion failures must not expose the reservation or raw metadata.
                    throw InvalidResponse("server matchmaking");
                }
            }

            if (string.Equals(status, "searching", StringComparison.OrdinalIgnoreCase))
                return new PlayServServerMatchResult("searching", null, wire.retry_after_ms);
            if (string.Equals(status, "bot", StringComparison.OrdinalIgnoreCase))
                return new PlayServServerMatchResult("bot", null, null);
            return new PlayServServerMatchResult("not_found", null, null);
        }

        /// <summary>Requests a game-server deployment. Acceptance does not imply room registration.</summary>
        public static async Task<PlayServServerLaunchResult> LaunchServerAsync(
            string functionSlug,
            string region = null,
            CancellationToken cancellationToken = default)
        {
            EnsureSupportedBuild();
            cancellationToken.ThrowIfCancellationRequested();
            var context = GetContext();
            var slug = ValidateFunctionSlug(functionSlug);
            var normalizedRegion = NormalizeOptional(region);
            if (normalizedRegion != null && normalizedRegion.Length > 64)
                throw new ArgumentOutOfRangeException(nameof(region), "Region must be at most 64 characters.");
            var response = await SendAsync(
                context,
                "POST",
                "rooms/" + Escape(slug) + "/servers:launch",
                PlayServGameServerJson.Serialize(new LaunchServerRequestWire { region = normalizedRegion }),
                context.HttpTimeout,
                "game server launch",
                includeRawDetails: true,
                cancellationToken);
            var wire = DeserializeRequired<LaunchServerResponseWire>(response.Body, "game server launch");
            if (string.IsNullOrWhiteSpace(wire.deployment_id))
                throw InvalidResponse("game server launch");
            return new PlayServServerLaunchResult(wire.deployment_id, wire.region);
        }

        /// <summary>Lists the rooms currently registered for a matchmaking function.</summary>
        public static async Task<IReadOnlyList<PlayServGameRoomInfo>> ListRoomsAsync(
            string functionSlug,
            CancellationToken cancellationToken = default)
        {
            EnsureSupportedBuild();
            cancellationToken.ThrowIfCancellationRequested();
            var context = GetContext();
            var slug = ValidateFunctionSlug(functionSlug);
            var response = await SendAsync(
                context,
                "GET",
                "rooms/" + Escape(slug),
                null,
                context.HttpTimeout,
                "room list",
                includeRawDetails: true,
                cancellationToken);
            var wire = DeserializeRequired<RoomListItemWire[]>(response.Body, "room list");
            var result = new PlayServGameRoomInfo[wire.Length];
            for (var i = 0; i < wire.Length; i++)
            {
                var room = wire[i];
                if (room == null || string.IsNullOrWhiteSpace(room.room_name))
                    throw InvalidResponse("room list");
                result[i] = new PlayServGameRoomInfo(
                    room.room_name,
                    room.players,
                    room.capacity,
                    room.age_sec,
                    room.state,
                    room.closing,
                    room.placement_state,
                    room.open,
                    room.drain_until,
                    room.drain_cause, room.connect?.ToModel(), room.region, room.instance_id, room.attributes);
            }

            return result;
        }

        /// <summary>Creates or refreshes a room snapshot without creating a managed handle.</summary>
        public static async Task<PlayServRoomUpsertResult> UpsertRoomAsync(
            string functionSlug,
            PlayServGameRoomSnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            EnsureSupportedBuild();
            cancellationToken.ThrowIfCancellationRequested();
            var context = GetContext();
            var slug = ValidateFunctionSlug(functionSlug);
            ValidateSnapshot(snapshot, nameof(snapshot));
            if (context.EnableUplink)
            {
                await Uplink.ConnectForRoomAsync(slug, cancellationToken);
                if (Uplink.RoomConfiguration == null) throw UplinkErrors.Exception("room_configuration_missing");
            }
            return await UpsertRoomCoreAsync(context, slug, snapshot, cancellationToken);
        }

        /// <summary>Closes a managed or unmanaged room and stops its local heartbeat when present.</summary>
        public static async Task CloseRoomAsync(
            string functionSlug,
            string roomName,
            CancellationToken cancellationToken = default)
        {
            EnsureSupportedBuild();
            cancellationToken.ThrowIfCancellationRequested();
            var context = GetContext();
            var slug = ValidateFunctionSlug(functionSlug);
            var room = ValidateRoomName(roomName, nameof(roomName));
            PlayServGameRoomHandle managedHandle;
            lock (Sync)
                Rooms.TryGetValue(BuildRoomKey(slug, room), out managedHandle);
            if (managedHandle != null)
            {
                var result = await managedHandle.CloseAsync(cancellationToken);
                if (!result.IsSuccess)
                    throw new PlayServGameServerException(result.Error);
                return;
            }
            await CloseRoomCoreAsync(context, slug, room, cancellationToken);
        }

        /// <summary>Consumes a reservation after the game's own player connection handshake.</summary>
        public static async Task<PlayServReservationConsumeResult> ConsumeReservationAsync(
            string functionSlug,
            string token,
            string playerId,
            string roomName = null,
            CancellationToken cancellationToken = default)
        {
            EnsureSupportedBuild();
            cancellationToken.ThrowIfCancellationRequested();
            var context = GetContext();
            var slug = ValidateFunctionSlug(functionSlug);
            var reservationToken = ValidateSecretToken(token, nameof(token));
            if (Uplink.AdmissionMode == PlayServAdmissionMode.Push)
                throw UplinkErrors.Exception("admission_mode_mismatch");
            var player = ValidatePlayerId(playerId, nameof(playerId));
            var room = roomName == null ? null : ValidateRoomName(roomName, nameof(roomName));
            var body = PlayServGameServerJson.Serialize(new ConsumeReservationRequestWire
            {
                player_id = player,
                room_name = room
            });
            var response = await SendAsync(
                context,
                "POST",
                "matchmaking/" + Escape(slug) + "/reservations/" + Escape(reservationToken) + ":consume",
                body,
                context.HttpTimeout,
                "reservation admission",
                includeRawDetails: false,
                cancellationToken);
            var wire = DeserializeRequired<ConsumeReservationResponseWire>(response.Body, "reservation admission");
            return new PlayServReservationConsumeResult(
                wire.ok,
                RedactValue(wire.error, reservationToken),
                wire.room_name,
                wire.player_id,
                wire.error_code);
        }

        /// <summary>Loads the safe runtime subset of a player profile, or null for HTTP 404.</summary>
        public static async Task<PlayServGameServerPlayerProfile> GetPlayerAsync(
            string playerId,
            CancellationToken cancellationToken = default)
        {
            EnsureSupportedBuild();
            cancellationToken.ThrowIfCancellationRequested();
            var context = GetContext();
            var player = ValidatePlayerId(playerId, nameof(playerId));
            var response = await SendAsync(
                context,
                "GET",
                "data/players/" + Escape(player),
                null,
                context.HttpTimeout,
                "player lookup",
                includeRawDetails: true,
                cancellationToken,
                allowNotFound: true);
            if (response.StatusCode == 404)
                return null;
            var wire = DeserializeRequired<PlayerProfileWire>(response.Body, "player lookup");
            if (string.IsNullOrWhiteSpace(wire.id) || string.IsNullOrWhiteSpace(wire.name))
                throw InvalidResponse("player lookup");
            return new PlayServGameServerPlayerProfile(
                wire.id,
                wire.name,
                wire.status,
                wire.sso,
                wire.country,
                wire.joined,
                wire.created_at,
                wire.updated_at);
        }

        /// <summary>Closes every managed room and clears process configuration.</summary>
        public static async Task<PlayServGameServerShutdownResult> ShutdownAsync(
            CancellationToken cancellationToken = default)
        {
            EnsureSupportedBuild();
            cancellationToken.ThrowIfCancellationRequested();
            lock (Sync) { if (_context != null) _context.ShuttingDown = true; }
            Uplink.CancelRoomCreation();
            await Uplink.DrainRoomCreationAsync();
            await Realtime.DisconnectAsync(cancellationToken);
            PlayServGameRoomHandle[] handles;
            PlayServGameServerContext context;
            lock (Sync)
            {
                context = _context;
                handles = new PlayServGameRoomHandle[Rooms.Count];
                Rooms.Values.CopyTo(handles, 0);
            }

            if (context == null)
            {
                Analytics.ResetForConfiguration();
                Logs.ResetForConfiguration();
                return new PlayServGameServerShutdownResult(
                    Array.Empty<PlayServGameRoomCloseResult>(),
                    PlayServError.None);
            }

            var analyticsError = await Analytics.FlushForShutdownAsync(cancellationToken);
            var logsError = await Logs.FlushForShutdownAsync(cancellationToken);

            var tasks = new Task<PlayServGameRoomCloseResult>[handles.Length];
            for (var i = 0; i < handles.Length; i++)
                tasks[i] = handles[i].CloseAsync(cancellationToken);

            var results = await Task.WhenAll(tasks);
            await Uplink.DisconnectAsync(cancellationToken);
            lock (Sync)
            {
                if (ReferenceEquals(_context, context))
                    _context = null;
                StartingRooms.Clear();
            }

            Analytics.ResetForConfiguration();
            Logs.ResetForConfiguration();

            return new PlayServGameServerShutdownResult(results, analyticsError, logsError);
        }

        internal static TimeSpan ResolveFindMatchTimeout(int waitMs)
        {
            return waitMs <= 0
                ? TimeSpan.FromSeconds(DefaultHttpTimeoutSeconds)
                : TimeSpan.FromMilliseconds(waitMs + NetworkMarginMs);
        }

        internal static void ConfigureForTesting(
            PlayServGameServerOptions options,
            IPlayServGameServerTransport transport,
            Func<DateTimeOffset> utcNow,
            Func<TimeSpan, CancellationToken, Task> delay)
        {
            ConfigureCore(options, transport, utcNow, delay);
            _context.Dispatch = action => action();
        }

        internal static void SetSupportedBuildForTesting(bool? supported)
        {
            _supportedBuildOverrideForTesting = supported;
        }

        internal static void ConfigurePlayerTokenValidatorForTesting(
            IPlayServGameServerJwksTransport transport,
            Func<DateTimeOffset> utcNow) =>
            PlayerTokenValidator.ConfigureForTesting(transport, utcNow);

        internal static PlayServCodeClient CreateCodeClient()
        {
            EnsureSupportedBuild();
            var context = GetContext();
            return new PlayServCodeClient(
                CreateRuntimeSettings(context),
                new PlayServGameServerRuntimeHttpClient(),
                new NewtonsoftJsonCodec(),
                requiresClientToken: false);
        }

        internal static PlayServCommerceClient CreateCommerceClient()
        {
            EnsureSupportedBuild();
            var context = GetContext();
            return new PlayServCommerceClient(
                CreateRuntimeSettings(context),
                new PlayServGameServerRuntimeHttpClient(),
                new NewtonsoftJsonCodec(),
                requiresClientToken: false);
        }

        internal static Task<PlayServRuntimeDataResponse> SendRuntimeDataAsync(
            PlayServRuntimeDataRequest request,
            CancellationToken cancellationToken) => SendRuntimeDataAsync(request, cancellationToken, null);

        private static async Task<PlayServRuntimeDataResponse> SendRuntimeDataAsync(
            PlayServRuntimeDataRequest request,
            CancellationToken cancellationToken,
            PlayServGameServerContext recordsContext)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            var context = recordsContext ?? GetContext();
            Action validateContext = recordsContext == null ? null : () =>
            {
                if (!ReferenceEquals(GetContext(), recordsContext))
                    throw new InvalidOperationException("The Records context belongs to a previous game-server configuration.");
            };
            validateContext?.Invoke();
            var timeout = request.TimeoutSeconds.HasValue
                ? TimeSpan.FromSeconds(request.TimeoutSeconds.Value)
                : context.HttpTimeout;
            try
            {
                var response = await SendAsync(
                    context,
                    request.Method,
                    request.RelativePath,
                    request.JsonBody,
                    timeout,
                    request.RelativePath.StartsWith("fn/", StringComparison.Ordinal)
                        ? "cloud function"
                        : "runtime data",
                    includeRawDetails: true,
                    cancellationToken,
                    runtimeRequest: request,
                    validateContext: validateContext);
                return new PlayServRuntimeDataResponse(
                    response.StatusCode,
                    response.Body,
                    response.ETag,
                    response.Location,
                    response.ContentType,
                    response.Headers);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (PlayServGameServerException exception)
            {
                var error = exception.UnifiedError;
                throw new PlayServRuntimeHttpException(
                    error.Message,
                    error.HttpStatus ?? 0,
                    error.RawDetails,
                    error.SourceCode,
                    error.Code == PlayServErrorCode.Network || error.Code == PlayServErrorCode.Timeout,
                    exception,
                    problemDetail: error.Message);
            }
        }

        internal static async Task<PlayServRoomUpsertResult> UpsertRoomCoreAsync(
            PlayServGameServerContext context,
            string functionSlug,
            PlayServGameRoomSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            PlayServGameRoomHandle handle;
            bool starting;
            lock (Sync)
            {
                var key = BuildRoomKey(functionSlug, snapshot.RoomName.Trim());
                Rooms.TryGetValue(key, out handle); starting = StartingRooms.Contains(key);
            }
            var roster = context.EnableUplink ? handle?.RosterSnapshot() ?? (starting ? Array.Empty<string>() : null) : null;
            var body = PlayServGameServerJson.Serialize(new UpsertRoomRequestWire
            {
                room_name = snapshot.RoomName.Trim(),
                players = snapshot.Players,
                capacity = context.EnableUplink && Uplink.RoomConfiguration != null ? Uplink.RoomConfiguration.Capacity : snapshot.Capacity,
                state = NormalizeOptional(snapshot.State),
                attributes = PlayServGameServerJson.ParseOptionalObject(snapshot.AttributesJson),
                open = snapshot.Open && (!context.EnableUplink || snapshot.Players < (Uplink.RoomConfiguration?.Capacity ?? snapshot.Capacity)),
                created_at = snapshot.CreatedAt,
                connect = snapshot.Connect?.ToWire(), region = snapshot.Region,
                instance_id = context.EnableUplink && context.UplinkStarted ? context.InstanceId : null,
                roster_hash = roster == null ? null : PlayServGameRoomHandle.ComputeRosterHash(roster),
                roster_count = roster?.Length
            });
            var response = await SendAsync(
                context,
                "POST",
                "rooms/" + Escape(functionSlug) + ":upsert",
                body,
                context.HttpTimeout,
                "room heartbeat",
                includeRawDetails: true,
                cancellationToken);
            var wire = DeserializeRequired<UpsertRoomResponseWire>(response.Body, "room heartbeat");
            if (wire.placement == null)
                throw InvalidResponse("room heartbeat");
            if (context.UplinkStarted) Uplink.ApplyConfiguration(wire.room_config);
            return new PlayServRoomUpsertResult(
                wire.created,
                new PlayServRoomPlacementAcknowledgment(
                    wire.placement.state,
                    wire.placement.open,
                    wire.placement.draining,
                    wire.placement.drain_cause,
                    wire.placement.drain_until,
                    wire.placement.open_refused))
            { RoomConfiguration = context.UplinkStarted ? Uplink.RoomConfiguration : null, RosterCheck = wire.roster_check };
        }

        internal static async Task CloseRoomCoreAsync(
            PlayServGameServerContext context,
            string functionSlug,
            string roomName,
            CancellationToken cancellationToken)
        {
            await SendAsync(
                context,
                "POST",
                "rooms/" + Escape(functionSlug) + "/" + Escape(roomName) + ":close",
                null,
                context.HttpTimeout,
                "room close",
                includeRawDetails: true,
                cancellationToken);
        }

        internal static void ValidateSnapshot(PlayServGameRoomSnapshot snapshot, string parameterName)
        {
            if (snapshot == null)
                throw new ArgumentNullException(parameterName);
            ValidateRoomName(snapshot.RoomName, parameterName);
            if (snapshot.Capacity <= 0)
                throw new ArgumentOutOfRangeException(parameterName, "Room capacity must be greater than zero.");
            if (snapshot.Players < 0)
                throw new ArgumentOutOfRangeException(parameterName, "Room player count cannot be negative.");
            if (snapshot.State != null && snapshot.State.Length > 256)
                throw new ArgumentOutOfRangeException(parameterName, "Room state must be at most 256 characters.");
        }

        internal static bool IsTerminalHeartbeatError(PlayServError error)
        {
            return error != null && error.IsError && !error.Retryable;
        }

        internal static void CancelForModuleShutdown()
        {
            Logs.ResetForConfiguration();
            Uplink.CancelForModuleShutdown();
            Realtime.CancelForModuleShutdown();
            Analytics.ResetForConfiguration();
            PlayServGameRoomHandle[] handles;
            lock (Sync)
            {
                handles = new PlayServGameRoomHandle[Rooms.Count];
                Rooms.Values.CopyTo(handles, 0);
                Rooms.Clear();
                StartingRooms.Clear();
                _context = null;
            }

            for (var i = 0; i < handles.Length; i++)
                handles[i].CancelLocally();
        }

        internal static void OnApplicationQuitting()
        {
            Uplink.CancelRoomCreation();
            PlayServGameRoomHandle[] handles;
            lock (Sync)
            {
                if (_context != null) _context.ShuttingDown = true;
                handles = new PlayServGameRoomHandle[Rooms.Count];
                Rooms.Values.CopyTo(handles, 0);
            }

            for (var i = 0; i < handles.Length; i++)
                handles[i].CancelHeartbeatLoop();

            ObserveBestEffort(CleanupOnQuitAsync(handles));
        }

        private static async Task CleanupOnQuitAsync(PlayServGameRoomHandle[] handles)
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            var tasks = new Task<PlayServGameRoomCloseResult>[handles.Length];
            for (var i = 0; i < handles.Length; i++)
                tasks[i] = handles[i].CloseAsync(cleanup.Token);
            try { await Logs.FlushForShutdownAsync(cleanup.Token); await Task.WhenAll(tasks); }
            finally { Logs.ResetForConfiguration(); await Uplink.DisconnectAsync(); }
        }

        private static async void ObserveBestEffort(Task task)
        {
            try
            {
                await task;
            }
            catch
            {
            }
        }

        private static void ConfigureCore(
            PlayServGameServerOptions options,
            IPlayServGameServerTransport transport,
            Func<DateTimeOffset> utcNow,
            Func<TimeSpan, CancellationToken, Task> delay)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));
            if (options.HeartbeatInterval < TimeSpan.FromSeconds(1) ||
                options.HeartbeatInterval > TimeSpan.FromSeconds(10))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    "HeartbeatInterval must be between 1 and 10 seconds.");
            }
            if (options.HttpTimeout <= TimeSpan.Zero || options.HttpTimeout.TotalMilliseconds > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(options), "HttpTimeout must be a positive supported duration.");
            if (options.LogQueueCapacity < 1 || options.LogQueueMaxBytes < 1)
                throw new ArgumentOutOfRangeException(nameof(options), "Log queue limits must be positive.");
            if (options.AdmissionEntryLimit < 1 || options.AdmissionByteLimit < 1)
                throw new ArgumentOutOfRangeException(nameof(options), "Admission limits must be positive.");
            if (options.RoomCreateTimeout <= TimeSpan.Zero || options.RoomCreateTimeout.TotalMilliseconds > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(options), "RoomCreateTimeout must be a positive supported duration.");

            var address = NormalizeOptional(options.BackendServerAddress) ??
                          NormalizeOptional(Environment.GetEnvironmentVariable(ApiUrlEnvironmentVariable));
            var baseAddress = NormalizeBackendAddress(address);
            var provider = options.ServerKeyProvider ?? new EnvironmentServerKeyProvider();
            var instanceId = options.InstanceId ?? ProcessInstanceId;
            if (!System.Text.RegularExpressions.Regex.IsMatch(instanceId, "^[A-Za-z0-9][A-Za-z0-9._:-]{0,63}$"))
                throw new ArgumentException("InstanceId must contain 1–64 protocol-safe characters.", nameof(options));
            var context = new PlayServGameServerContext(
                provider,
                transport ?? new PlayServUnityGameServerTransport(baseAddress),
                baseAddress,
                options.HeartbeatInterval,
                options.HttpTimeout,
                utcNow ?? (() => DateTimeOffset.UtcNow),
                delay ?? ((duration, ct) => Task.Delay(duration, ct)))
            {
                EnableUplink = options.EnableUplink, InstanceId = instanceId,
                RpcMethods = options.RpcRegistry?.Snapshot(),
                EnablePushedAdmission = options.EnablePushedAdmission,
                TicketOfferHandler = options.TicketOfferHandler,
                AdmissionEntryLimit = options.AdmissionEntryLimit, AdmissionByteLimit = options.AdmissionByteLimit,
                ExecutorSlug = NormalizeOptional(options.ExecutorSlug) ?? NormalizeOptional(Environment.GetEnvironmentVariable("PLAYSERV_EXECUTOR_SLUG")),
                UplinkCredentialProvider = options.UplinkCredentialProvider,
                RoomFactory = options.RoomFactory, RoomCreateTimeout = options.RoomCreateTimeout,
                LogQueueCapacity = options.LogQueueCapacity, LogQueueMaxBytes = options.LogQueueMaxBytes
            };
            var delivery = SynchronizationContext.Current;
            context.Dispatch = action => { if (delivery == null) action(); else delivery.Post(_ => action(), null); };

            lock (Sync)
            {
                if (Rooms.Count != 0 || StartingRooms.Count != 0)
                    throw new InvalidOperationException("Close all active PlayServ rooms before reconfiguring the game server client.");
                if (Uplink.State != PlayServUplinkState.Disconnected)
                    throw new InvalidOperationException("Disconnect the uplink before reconfiguring the game server.");
                _context = context;
            }

            Analytics.ResetForConfiguration();
            Logs.ResetForConfiguration(context);
        }

        private static async Task<PlayServGameServerHttpResponse> SendAsync(
            PlayServGameServerContext context,
            string method,
            string relativePath,
            string jsonBody,
            TimeSpan timeout,
            string operation,
            bool includeRawDetails,
            CancellationToken cancellationToken,
            bool allowNotFound = false,
            PlayServRuntimeDataRequest runtimeRequest = null,
            Action validateContext = null)
        {
            EnsureSupportedBuild();
            cancellationToken.ThrowIfCancellationRequested();
            var key = context.UplinkStarted
                ? await Uplink.GetRestCredentialAsync(context, cancellationToken)
                : await ResolveServerKeyForRealtimeAsync(context, cancellationToken);
            try
            {
                var response = await context.InvokeAsync(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    validateContext?.Invoke();
                    return context.Transport.SendAsync(
                    new PlayServGameServerHttpRequest
                    {
                        Method = method,
                        RelativePath = relativePath,
                        JsonBody = jsonBody,
                        ContentType = runtimeRequest?.ContentType,
                        IfMatch = runtimeRequest?.IfMatch,
                        IdempotencyKey = runtimeRequest?.IdempotencyKey,
                        FunctionVersion = runtimeRequest?.FunctionVersion,
                        Headers = runtimeRequest?.Headers,
                        ServerKey = key,
                        Timeout = timeout,
                        Operation = operation
                    },
                    cancellationToken);
                });
                if (response == null)
                    throw InvalidResponse(operation);
                if (response.StatusCode >= 200 && response.StatusCode <= 299)
                    return response;
                if (allowNotFound && response.StatusCode == 404)
                    return response;

                var problem = TryParseProblem(response.Body);
                var sourceCode = FirstNonEmpty(problem == null ? null : problem.code, problem == null ? null : problem.error);
                var error = PlayServError.FromHttp(
                    response.StatusCode,
                    sourceCode,
                    "PlayServ " + operation + " failed.",
                    false,
                    false,
                    includeRawDetails ? RedactValue(response.Body, key) : null,
                    problem == null ? null : problem.retryable);
                throw new PlayServGameServerException(error);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (PlayServGameServerException)
            {
                throw;
            }
            catch (PlayServGameServerTransportFailure ex)
            {
                var error = PlayServError.FromHttp(
                    0,
                    ex.IsTimeout ? "request_timeout" : "network_error",
                    ex.IsTimeout
                        ? "The PlayServ " + operation + " request timed out."
                        : "The PlayServ " + operation + " request failed to reach the network.",
                    true,
                    ex.IsTimeout);
                throw new PlayServGameServerException(error);
            }
        }

        private static T DeserializeRequired<T>(string body, string operation) where T : class
        {
            if (string.IsNullOrWhiteSpace(body))
                throw InvalidResponse(operation);
            try
            {
                var result = PlayServGameServerJson.Deserialize<T>(body);
                if (result == null)
                    throw InvalidResponse(operation);
                return result;
            }
            catch (PlayServGameServerException)
            {
                throw;
            }
            catch (Exception)
            {
                throw new PlayServGameServerException(
                    new PlayServError(
                        PlayServErrorCode.Deserialization,
                        "invalid_response",
                        "The PlayServ " + operation + " response could not be decoded."));
            }
        }

        private static ProblemDetailsWire TryParseProblem(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return null;
            try
            {
                return PlayServGameServerJson.Deserialize<ProblemDetailsWire>(body);
            }
            catch
            {
                return null;
            }
        }

        private static PlayServGameServerException InvalidResponse(string operation)
        {
            return new PlayServGameServerException(
                new PlayServError(
                    PlayServErrorCode.InvalidResponse,
                    "invalid_response",
                    "The PlayServ " + operation + " response was empty or malformed."));
        }

        private static PlayServGameServerContext GetContext()
        {
            lock (Sync)
            {
                if (_context == null)
                    throw new InvalidOperationException("Call PlayServGameServer.Configure before using the game server API.");
                return _context;
            }
        }

        private static void RemoveRoom(string key, PlayServGameRoomHandle handle)
        {
            Uplink.ClearAdmissionRoom(handle.RoomName);
            lock (Sync)
            {
                if (Rooms.TryGetValue(key, out var current) && ReferenceEquals(current, handle))
                    Rooms.Remove(key);
            }
        }

        private static string ValidateFunctionSlug(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Function slug is required.", nameof(value));
            var normalized = value.Trim();
            if (normalized.IndexOf('/') >= 0 || normalized.IndexOf('?') >= 0 || normalized.IndexOf('#') >= 0)
                throw new ArgumentException("Function slug must not contain a path, query, or fragment.", nameof(value));
            return normalized;
        }

        private static string ValidateRoomName(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Room name is required.", parameterName);
            var normalized = value.Trim();
            if (normalized.Length > 64 || !IsAsciiAlphaNumeric(normalized[0]))
                throw new ArgumentException("Room name must match ^[A-Za-z0-9][A-Za-z0-9:._-]{0,63}$.", parameterName);
            for (var i = 1; i < normalized.Length; i++)
            {
                var character = normalized[i];
                if (!IsAsciiAlphaNumeric(character) && character != ':' && character != '.' &&
                    character != '_' && character != '-')
                {
                    throw new ArgumentException("Room name must match ^[A-Za-z0-9][A-Za-z0-9:._-]{0,63}$.", parameterName);
                }
            }
            return normalized;
        }

        private static string ValidatePlayerId(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Player ID is required.", parameterName);
            var normalized = value.Trim();
            if (normalized.IndexOf('/') >= 0 || normalized.IndexOf('?') >= 0 || normalized.IndexOf('#') >= 0)
                throw new ArgumentException("Player ID must not contain a path, query, or fragment.", parameterName);
            return normalized;
        }

        private static bool IsAsciiAlphaNumeric(char value)
        {
            return value >= 'a' && value <= 'z' ||
                   value >= 'A' && value <= 'Z' ||
                   value >= '0' && value <= '9';
        }

        private static string ValidateSecretToken(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Reservation token is required.", parameterName);
            if (value.IndexOf('\r') >= 0 || value.IndexOf('\n') >= 0)
                throw new ArgumentException("Reservation token cannot contain line breaks.", parameterName);
            var normalized = value.Trim();
            return normalized;
        }

        private static string ValidateServerKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException("A PlayServ sk_* server key is required.");
            if (value.IndexOf('\r') >= 0 || value.IndexOf('\n') >= 0)
                throw new InvalidOperationException("PlayServ server credentials cannot contain line breaks.");
            var normalized = value.Trim();
            if (!normalized.StartsWith("sk_", StringComparison.Ordinal))
                throw new InvalidOperationException("The dedicated game server package requires a PlayServ sk_* credential.");
            return normalized;
        }

        private static string NormalizeBackendAddress(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(
                    "Set PlayServGameServerOptions.BackendServerAddress or PLAYSERV_API_URL.");
            }
            if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
                throw new InvalidOperationException("The PlayServ game server backend address is invalid.");
            string scheme;
            if (string.Equals(uri.Scheme, "wss", StringComparison.OrdinalIgnoreCase))
                scheme = Uri.UriSchemeHttps;
            else if (string.Equals(uri.Scheme, "ws", StringComparison.OrdinalIgnoreCase))
                scheme = Uri.UriSchemeHttp;
            else if (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                scheme = uri.Scheme;
            else
                throw new InvalidOperationException("The PlayServ game server backend address must use http, https, ws, or wss.");
            var builder = new UriBuilder(uri)
            {
                Scheme = scheme,
                Path = string.Empty,
                Query = string.Empty,
                Fragment = string.Empty
            };
            return builder.Uri.ToString().TrimEnd('/');
        }

        private static string NormalizeOptional(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static string Escape(string value)
        {
            return Uri.EscapeDataString(value);
        }

        private static string BuildRoomKey(string slug, string roomName)
        {
            return slug + "\n" + roomName;
        }

        private static string FirstNonEmpty(string first, string second)
        {
            return !string.IsNullOrWhiteSpace(first) ? first : second;
        }

        private static string RedactValue(string text, string sensitiveValue)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(sensitiveValue))
                return text;
            return text.Replace(sensitiveValue, "[REDACTED]");
        }

        internal static PlayServRecordSet<T> CreateRecordSetForActingPlayer<T>(
            string entityId,
            string playerJwt) =>
            CreateRecordSet<T>(entityId, playerJwt);

        private static PlayServRecordSet<T> CreateRecordSet<T>(
            string entityId,
            string actingPlayerJwt = null)
        {
            EnsureSupportedBuild();
            var client = CreateRecordsClient(actingPlayerJwt);
            return new PlayServRecordSet<T>(
                client,
                entityId,
                Realtime.SubscribeCollectionAsync<T>,
                Realtime.SubscribeRecordAsync<T>);
        }

        private static PlayServRecordsClient CreateRecordsClient(
            string actingPlayerJwt = null)
        {
            var context = GetContext();
            return new PlayServRecordsClient(
                CreateRuntimeSettings(context),
                new PlayServGameServerRuntimeHttpClient(actingPlayerJwt,
                    (request, ct) => SendRuntimeDataAsync(request, ct, context)),
                new NewtonsoftJsonCodec(),
                requiresClientToken: false,
                accessSubject: PlayServDataAccessSubject.Server,
                catalogueCacheScope: context.CatalogueCacheScope,
                isReferenceContextCurrent: () => ReferenceEquals(GetContext(), context));
        }

        internal static PlayServGameServerContext GetContextForRealtime() => GetContext();

        internal static PlayServGameServerContext GetContextForServices() => GetContext();

        internal static void ReplaceUplinkForShutdown(PlayServGameServerUplink previous)
        {
            if (ReferenceEquals(Uplink, previous)) Uplink = new PlayServGameServerUplink();
        }
        internal static void ApplyRoomConfiguration(PlayServRoomConfiguration config)
        {
            PlayServGameRoomHandle[] rooms;
            lock (Sync) { rooms = new PlayServGameRoomHandle[Rooms.Count]; Rooms.Values.CopyTo(rooms, 0); }
            foreach (var room in rooms) room.ApplyConfiguration(config);
        }
        internal static void RepairUplinkRosters()
        {
            PlayServGameRoomHandle[] rooms;
            lock (Sync) { rooms = new PlayServGameRoomHandle[Rooms.Count]; Rooms.Values.CopyTo(rooms, 0); }
            foreach (var room in rooms) room.RepairRoster();
        }
        internal static void TerminateUplinkRooms(PlayServError error)
        {
            PlayServGameRoomHandle[] rooms;
            lock (Sync) { rooms = new PlayServGameRoomHandle[Rooms.Count]; Rooms.Values.CopyTo(rooms, 0); }
            foreach (var room in rooms) room.TerminateFromUplink(error);
        }

        internal static void EnsureSupportedBuildForRealtime() => EnsureSupportedBuild();

        internal static void EnsureSupportedBuildForServices() => EnsureSupportedBuild();

        internal static string ValidateOptionalAnalyticsPlayerId(string playerId) =>
            string.IsNullOrWhiteSpace(playerId)
                ? null
                : ValidatePlayerId(playerId, nameof(playerId));

        internal static async Task<string> ResolveServerKeyForRealtimeAsync(
            PlayServGameServerContext context,
            CancellationToken cancellationToken)
        {
            try
            {
                return ValidateServerKey(
                    await context.ServerKeyProvider.GetServerKeyAsync(cancellationToken));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch
            {
                throw new InvalidOperationException(
                    "The PlayServ server key provider failed to resolve a credential.");
            }
        }

        private static string ValidatePlayerJwt(string playerJwt)
        {
            if (string.IsNullOrWhiteSpace(playerJwt))
                throw new ArgumentException("A player session JWT is required.", nameof(playerJwt));

            var normalized = playerJwt.Trim();
            if (normalized.IndexOf('\r') >= 0 || normalized.IndexOf('\n') >= 0 ||
                normalized.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("The player session JWT has an invalid format.", nameof(playerJwt));
            }

            var segments = normalized.Split('.');
            if (segments.Length != 3 ||
                string.IsNullOrEmpty(segments[0]) ||
                string.IsNullOrEmpty(segments[1]) ||
                string.IsNullOrEmpty(segments[2]))
            {
                throw new ArgumentException("The player session JWT has an invalid format.", nameof(playerJwt));
            }

            for (var i = 0; i < segments.Length; i++)
            {
                for (var j = 0; j < segments[i].Length; j++)
                {
                    var value = segments[i][j];
                    if (!(value >= 'a' && value <= 'z') &&
                        !(value >= 'A' && value <= 'Z') &&
                        !(value >= '0' && value <= '9') &&
                        value != '-' && value != '_')
                    {
                        throw new ArgumentException("The player session JWT has an invalid format.", nameof(playerJwt));
                    }
                }
            }

            return normalized;
        }

        private static PlayServSettings CreateRuntimeSettings(PlayServGameServerContext context) =>
            new PlayServSettings
            {
                BackendServerAddress = context.BackendServerAddress,
                EnableAutomaticPlayerAuthentication = false
            };

        private static void EnsureSupportedBuild()
        {
            if (_supportedBuildOverrideForTesting.HasValue)
            {
                if (!_supportedBuildOverrideForTesting.Value)
                {
                    throw new PlatformNotSupportedException(
                        "com.playserv.game-server is available only in UNITY_SERVER builds. Editor use is allowed for tests.");
                }

                return;
            }
#if !UNITY_SERVER && !UNITY_EDITOR
            throw new PlatformNotSupportedException(
                "com.playserv.game-server is available only in UNITY_SERVER builds. Editor use is allowed for tests.");
#endif
        }

        private sealed class EnvironmentServerKeyProvider : IPlayServServerKeyProvider
        {
            public Task<string> GetServerKeyAsync(CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(Environment.GetEnvironmentVariable(ServerKeyEnvironmentVariable));
            }
        }
    }

    internal sealed class PlayServGameServerContext
    {
        internal PlayServGameServerContext(
            IPlayServServerKeyProvider serverKeyProvider,
            IPlayServGameServerTransport transport,
            string backendServerAddress,
            TimeSpan heartbeatInterval,
            TimeSpan httpTimeout,
            Func<DateTimeOffset> utcNow,
            Func<TimeSpan, CancellationToken, Task> delay)
        {
            ServerKeyProvider = serverKeyProvider;
            Transport = transport;
            BackendServerAddress = backendServerAddress;
            CatalogueCacheScope = string.Concat(
                backendServerAddress,
                "\nserver\n",
                Guid.NewGuid().ToString("N"));
            HeartbeatInterval = heartbeatInterval;
            HttpTimeout = httpTimeout;
            UtcNow = utcNow;
            Delay = delay;
        }

        internal IPlayServServerKeyProvider ServerKeyProvider { get; }
        internal IPlayServGameServerTransport Transport { get; }
        internal string BackendServerAddress { get; }
        internal string CatalogueCacheScope { get; }
        internal TimeSpan HeartbeatInterval { get; }
        internal TimeSpan HttpTimeout { get; }
        internal Func<DateTimeOffset> UtcNow { get; }
        internal Func<TimeSpan, CancellationToken, Task> Delay { get; }
        internal bool EnableUplink;
        internal bool EnablePushedAdmission;
        internal PlayServTicketOfferHandler TicketOfferHandler;
        internal int AdmissionEntryLimit, AdmissionByteLimit;
        internal bool UplinkStarted;
        internal string ExecutorSlug, InstanceId;
        internal IPlayServUplinkCredentialProvider UplinkCredentialProvider;
        internal PlayServRoomFactory RoomFactory;
        internal Dictionary<string, PlayServServerRpcRegistry.Method> RpcMethods;
        internal TimeSpan RoomCreateTimeout;
        internal int LogQueueCapacity, LogQueueMaxBytes;
        internal volatile bool ShuttingDown;
        internal Action<Action> Dispatch;
        internal Func<double> MonotonicSeconds = () => (double)System.Diagnostics.Stopwatch.GetTimestamp() / System.Diagnostics.Stopwatch.Frequency;
        private readonly Dictionary<string, long> _presenceSequence = new Dictionary<string, long>(StringComparer.Ordinal);
        internal long NextPresenceSequence(string room)
        {
            lock (_presenceSequence)
            {
                _presenceSequence.TryGetValue(room, out var previous);
                return _presenceSequence[room] = Math.Max(previous + 1, UtcNow().ToUnixTimeMilliseconds());
            }
        }
        internal void Post(Action action) => Dispatch(() => { try { action(); } catch { } });
        internal Task<T> InvokeAsync<T>(Func<Task<T>> action)
        {
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            Dispatch(async () =>
            {
                try { completion.TrySetResult(await action()); }
                catch (OperationCanceledException) { completion.TrySetCanceled(); }
                catch (Exception ex) { completion.TrySetException(ex); }
            });
            return completion.Task;
        }
    }
}
