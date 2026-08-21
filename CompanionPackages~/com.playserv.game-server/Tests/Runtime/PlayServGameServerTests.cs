using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.Code;
using Playserv.Commerce;
using Playserv.Data;
using Playserv.GameServer;
using Playserv.Modules;
using Playserv.Wrapper;

namespace Playserv.Tests.Runtime.GameServer
{
    public sealed class PlayServGameServerTests
    {
        [SetUp]
        public void SetUp()
        {
            PlayServGameServer.SetSupportedBuildForTesting(null);
            PlayServGameServer.CancelForModuleShutdown();
        }

        [TearDown]
        public void TearDown()
        {
            PlayServGameServer.SetSupportedBuildForTesting(null);
            PlayServGameServer.CancelForModuleShutdown();
        }

        [Test]
        public void ClientBuildGate_RejectsBeforeCredentialResolutionAndTransport()
        {
            var transport = new FakeTransport();
            var provider = new RotatingKeyProvider("sk_server");
            Configure(transport, provider);
            PlayServGameServer.SetSupportedBuildForTesting(false);

            Capture<PlatformNotSupportedException>(() =>
                PlayServGameServer.ListRoomsAsync("arena"));

            Assert.That(provider.CallCount, Is.Zero);
            Assert.That(transport.Requests, Is.Empty);
        }

        [TestCase(0, 10_000)]
        [TestCase(20_000, 25_000)]
        [TestCase(25_000, 30_000)]
        public void FindDeadline_IsDerivedFromWireWait(int waitMs, int expectedMilliseconds)
        {
            Assert.That(
                PlayServGameServer.ResolveFindMatchTimeout(waitMs),
                Is.EqualTo(TimeSpan.FromMilliseconds(expectedMilliseconds)));
        }

        [Test]
        public void CallerCancellation_StopsBeforeCredentialResolutionAndTransport()
        {
            var transport = new FakeTransport();
            var provider = new RotatingKeyProvider("sk_server");
            Configure(transport, provider);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Capture<OperationCanceledException>(() =>
                PlayServGameServer.ListRoomsAsync("arena", cancellation.Token));

            Assert.That(provider.CallCount, Is.Zero);
            Assert.That(transport.Requests, Is.Empty);
        }

        [Test]
        public void Configure_RejectsHeartbeatOutsideBackendSafeRange()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                PlayServGameServer.Configure(new PlayServGameServerOptions
                {
                    BackendServerAddress = "https://api.playserv.test",
                    HeartbeatInterval = TimeSpan.FromMilliseconds(999)
                }));
        }

        [TestCase("room name")]
        [TestCase("кімната")]
        [TestCase("room/name")]
        public void RoomValidation_RejectsNamesOutsideBackendRegex(string roomName)
        {
            var transport = new FakeTransport();
            Configure(transport, new RotatingKeyProvider("sk_server"));

            Capture<ArgumentException>(() =>
                PlayServGameServer.UpsertRoomAsync(
                    "arena",
                    new PlayServGameRoomSnapshot(roomName, 0, 8)));

            Assert.That(transport.Requests, Is.Empty);
        }

        [Test]
        public void SdkProfiles_EnableGameServerOnlyForServerAndFullTargets()
        {
            Assert.That(PlayServSdkProfiles.ClientSdk.EnablesModule(PlayServModuleIds.GameServer), Is.False);
            Assert.That(PlayServSdkProfiles.ServerSdk.EnablesModule(PlayServModuleIds.GameServer), Is.True);
            Assert.That(PlayServSdkProfiles.FullSdk.EnablesModule(PlayServModuleIds.GameServer), Is.True);
            Assert.That(PlayServSdkProfiles.ClientSdk.EnablesModule(PlayServModuleIds.Analytics), Is.True);
            Assert.That(PlayServSdkProfiles.ServerSdk.EnablesModule(PlayServModuleIds.Analytics), Is.True);
        }

        [Test]
        public void Launch_ResolvesRotatingServerKeyForEveryRequest()
        {
            var transport = new FakeTransport();
            transport.Enqueue(202, "{\"deployment_id\":\"dep_1\",\"region\":\"eu\"}");
            transport.Enqueue(202, "{\"deployment_id\":\"dep_2\",\"region\":\"us\"}");
            var provider = new RotatingKeyProvider("sk_first", "sk_second");
            Configure(transport, provider);

            var first = PlayServGameServer.LaunchServerAsync("arena", "eu").GetAwaiter().GetResult();
            var second = PlayServGameServer.LaunchServerAsync("arena", "us").GetAwaiter().GetResult();

            Assert.That(first.DeploymentId, Is.EqualTo("dep_1"));
            Assert.That(second.DeploymentId, Is.EqualTo("dep_2"));
            Assert.That(transport.Requests[0].ServerKey, Is.EqualTo("sk_first"));
            Assert.That(transport.Requests[1].ServerKey, Is.EqualTo("sk_second"));
            Assert.That(transport.Requests[0].RelativePath, Is.EqualTo("matchmaking/arena/servers:launch"));
            Assert.That(transport.Requests[0].JsonBody, Does.Contain("\"region\":\"eu\""));
        }

        [Test]
        public void InvalidServerKey_IsRejectedBeforeTransport()
        {
            var transport = new FakeTransport();
            Configure(transport, new RotatingKeyProvider("pk_player"));

            Capture<InvalidOperationException>(() =>
                PlayServGameServer.ListRoomsAsync("arena"));
            Assert.That(transport.Requests, Is.Empty);
        }

        [TestCase("sk_secret\n")]
        [TestCase("sk_secret\rheader")]
        public void ServerKeyWithLineBreak_IsRejectedWithoutLeakingOrSending(string key)
        {
            var transport = new FakeTransport();
            Configure(transport, new RotatingKeyProvider(key));

            var exception = Capture<InvalidOperationException>(() =>
                PlayServGameServer.ListRoomsAsync("arena"));

            Assert.That(exception.Message, Does.Not.Contain("sk_secret"));
            Assert.That(transport.Requests, Is.Empty);
        }

        [Test]
        public void EnvironmentFallback_ProvidesAddressAndPerRequestServerKey()
        {
            var previousAddress = Environment.GetEnvironmentVariable("PLAYSERV_API_URL");
            var previousKey = Environment.GetEnvironmentVariable("PLAYSERV_SERVER_KEY");
            try
            {
                Environment.SetEnvironmentVariable("PLAYSERV_API_URL", "https://api.playserv.test/ws");
                Environment.SetEnvironmentVariable("PLAYSERV_SERVER_KEY", "sk_environment");
                var transport = new FakeTransport();
                transport.Enqueue(200, "[]");
                PlayServGameServer.ConfigureForTesting(
                    new PlayServGameServerOptions(),
                    transport,
                    () => DateTimeOffset.UtcNow,
                    (_, ct) => Task.Delay(Timeout.Infinite, ct));

                var rooms = PlayServGameServer.ListRoomsAsync("arena").GetAwaiter().GetResult();

                Assert.That(rooms, Is.Empty);
                Assert.That(transport.Requests[0].ServerKey, Is.EqualTo("sk_environment"));
            }
            finally
            {
                Environment.SetEnvironmentVariable("PLAYSERV_API_URL", previousAddress);
                Environment.SetEnvironmentVariable("PLAYSERV_SERVER_KEY", previousKey);
            }
        }

        [Test]
        public void Find_UsesServerPlayerIdObjectParametersAndLongPollDeadline()
        {
            var transport = new FakeTransport();
            transport.Enqueue(
                200,
                "{\"status\":\"matched\",\"room_name\":\"room-1\",\"reservation_token\":\"secret\",\"expires_at\":\"2026-08-19T12:00:00Z\"}");
            Configure(transport, new RotatingKeyProvider("sk_server"));

            var result = PlayServGameServer.FindMatchForPlayerAsync(
                new PlayServServerFindMatchRequest
                {
                    FunctionSlug = "arena",
                    PlayerId = "plr_1",
                    Matchmaker = "ranked",
                    Parameters = new Dictionary<string, object> { ["skill"] = 42 },
                    WaitMs = 20_000,
                    SearchAgeMs = 900
                }).GetAwaiter().GetResult();

            Assert.That(result.IsMatched, Is.True);
            Assert.That(result.Reservation.RoomName, Is.EqualTo("room-1"));
            var sent = transport.Requests[0];
            Assert.That(sent.Timeout, Is.EqualTo(TimeSpan.FromSeconds(25)));
            Assert.That(sent.JsonBody, Does.Contain("\"player_id\":\"plr_1\""));
            Assert.That(sent.JsonBody, Does.Contain("\"params\":{\"skill\":42}"));
            Assert.That(sent.JsonBody, Does.Contain("\"search_age_ms\":900"));
        }

        [Test]
        public void Find_RejectsNonObjectParametersBeforeTransport()
        {
            var transport = new FakeTransport();
            Configure(transport, new RotatingKeyProvider("sk_server"));

            Capture<ArgumentException>(() =>
                PlayServGameServer.FindMatchForPlayerAsync(
                    new PlayServServerFindMatchRequest
                    {
                        FunctionSlug = "arena",
                        PlayerId = "plr_1",
                        Parameters = new[] { 1, 2 }
                    }));
            Assert.That(transport.Requests, Is.Empty);
        }

        [Test]
        public void RoomHandle_DegradesRecoversAndClosesWithoutConcurrentHeartbeat()
        {
            var transport = new FakeTransport();
            transport.Enqueue(200, ActiveUpsert(created: true));
            transport.EnqueueFailure(isTimeout: false);
            transport.Enqueue(200, ActiveUpsert(created: true));
            transport.Enqueue(204, null);
            Configure(transport, new RotatingKeyProvider("sk_server"));

            var handle = PlayServGameServer.StartRoomAsync(
                new PlayServStartRoomRequest(
                    "arena",
                    new PlayServGameRoomSnapshot("room-1", 1, 8, attributes: new { map = "forest" })))
                .GetAwaiter().GetResult();
            var failures = 0;
            handle.HeartbeatFailed += (_, __) => failures++;

            var exception = Capture<PlayServGameServerException>(() =>
                handle.HeartbeatAsync());
            Assert.That(exception.UnifiedError.Retryable, Is.True);
            Assert.That(handle.State, Is.EqualTo(PlayServGameRoomState.Degraded));

            var recovered = handle.HeartbeatAsync().GetAwaiter().GetResult();
            Assert.That(recovered.Created, Is.True);
            Assert.That(handle.State, Is.EqualTo(PlayServGameRoomState.Active));
            Assert.That(failures, Is.EqualTo(1));

            var closed = handle.CloseAsync().GetAwaiter().GetResult();
            Assert.That(closed.IsSuccess, Is.True);
            Assert.That(handle.State, Is.EqualTo(PlayServGameRoomState.Closed));
            Assert.That(handle.CloseAsync().GetAwaiter().GetResult().WasAlreadyClosed, Is.True);
        }

        [Test]
        public void StartRoom_RejectsDuplicateActiveLogicalRoomBeforeSecondUpsert()
        {
            var transport = new FakeTransport();
            transport.Enqueue(200, ActiveUpsert(created: true));
            transport.Enqueue(204, null);
            Configure(transport, new RotatingKeyProvider("sk_server"));
            var request = new PlayServStartRoomRequest(
                "arena",
                new PlayServGameRoomSnapshot("room-1", 0, 8));
            var handle = PlayServGameServer.StartRoomAsync(request).GetAwaiter().GetResult();

            Capture<InvalidOperationException>(() => PlayServGameServer.StartRoomAsync(request));

            Assert.That(transport.Requests.Count, Is.EqualTo(1));
            handle.CloseAsync().GetAwaiter().GetResult();
        }

        [Test]
        public void PlacementDrainAcknowledgment_DoesNotOverwriteDesiredSnapshot()
        {
            var transport = new FakeTransport();
            transport.Enqueue(
                200,
                "{\"created\":false,\"placement\":{\"state\":\"draining\",\"open\":false,\"draining\":true,\"drain_cause\":\"maintenance\",\"open_refused\":true}}" );
            transport.Enqueue(204, null);
            Configure(transport, new RotatingKeyProvider("sk_server"));
            var handle = PlayServGameServer.StartRoomAsync(
                    new PlayServStartRoomRequest(
                        "arena",
                        new PlayServGameRoomSnapshot("room-1", 0, 8, open: true)))
                .GetAwaiter().GetResult();

            Assert.That(handle.State, Is.EqualTo(PlayServGameRoomState.Draining));
            Assert.That(handle.DesiredSnapshot.Open, Is.True);
            Assert.That(handle.LastPlacementAcknowledgment.OpenRefused, Is.True);
            handle.CloseAsync().GetAwaiter().GetResult();
        }

        [Test]
        public void Shutdown_ClosesMultipleManagedRoomsAndClearsConfiguration()
        {
            var transport = new FakeTransport();
            transport.Enqueue(200, ActiveUpsert(created: true));
            transport.Enqueue(200, ActiveUpsert(created: true));
            transport.Enqueue(204, null);
            transport.Enqueue(204, null);
            Configure(transport, new RotatingKeyProvider("sk_server"));
            var first = PlayServGameServer.StartRoomAsync(
                    new PlayServStartRoomRequest(
                        "arena",
                        new PlayServGameRoomSnapshot("room-1", 0, 8)))
                .GetAwaiter().GetResult();
            var second = PlayServGameServer.StartRoomAsync(
                    new PlayServStartRoomRequest(
                        "arena",
                        new PlayServGameRoomSnapshot("room-2", 0, 8)))
                .GetAwaiter().GetResult();

            var shutdown = PlayServGameServer.ShutdownAsync().GetAwaiter().GetResult();

            Assert.That(shutdown.IsSuccess, Is.True);
            Assert.That(shutdown.Rooms.Count, Is.EqualTo(2));
            Assert.That(first.State, Is.EqualTo(PlayServGameRoomState.Closed));
            Assert.That(second.State, Is.EqualTo(PlayServGameRoomState.Closed));
            Assert.That(PlayServGameServer.IsConfigured, Is.False);
        }

        [Test]
        public void TerminalHeartbeat_RemovesRoomAndRaisesTerminalSequenceOnce()
        {
            var transport = new FakeTransport();
            transport.Enqueue(200, ActiveUpsert(created: false));
            transport.Enqueue(
                401,
                "{\"code\":\"invalid_server_key\",\"detail\":\"credential sk_server rejected\"}");
            Configure(transport, new RotatingKeyProvider("sk_server"));
            var handle = PlayServGameServer.StartRoomAsync(
                new PlayServStartRoomRequest(
                    "arena",
                    new PlayServGameRoomSnapshot("room-1", 0, 8)))
                .GetAwaiter().GetResult();
            var failures = 0;
            var terminations = 0;
            handle.HeartbeatFailed += (_, __) => failures++;
            handle.Terminated += (_, __) => terminations++;

            var terminal = Capture<PlayServGameServerException>(() => handle.HeartbeatAsync());

            Assert.That(handle.State, Is.EqualTo(PlayServGameRoomState.Terminated));
            Assert.That(terminal.UnifiedError.Code, Is.EqualTo(PlayServErrorCode.Unauthorized));
            Assert.That(terminal.UnifiedError.SourceCode, Is.EqualTo("invalid_server_key"));
            Assert.That(terminal.UnifiedError.Retryable, Is.False);
            Assert.That(terminal.UnifiedError.RawDetails, Does.Not.Contain("sk_server"));
            Assert.That(failures, Is.EqualTo(1));
            Assert.That(terminations, Is.EqualTo(1));
        }

        [Test]
        public void InvalidRotatedCredential_TerminatesHeartbeatWithoutSecondHttpRequest()
        {
            var transport = new FakeTransport();
            transport.Enqueue(200, ActiveUpsert(created: true));
            Configure(transport, new RotatingKeyProvider("sk_initial", "pk_invalid"));
            var handle = PlayServGameServer.StartRoomAsync(
                    new PlayServStartRoomRequest(
                        "arena",
                        new PlayServGameRoomSnapshot("room-1", 0, 8)))
                .GetAwaiter().GetResult();
            var terminations = 0;
            handle.Terminated += (_, __) => terminations++;

            Capture<InvalidOperationException>(() => handle.HeartbeatAsync());

            Assert.That(handle.State, Is.EqualTo(PlayServGameRoomState.Terminated));
            Assert.That(handle.LastError.Code, Is.EqualTo(PlayServErrorCode.InvalidConfiguration));
            Assert.That(terminations, Is.EqualTo(1));
            Assert.That(transport.Requests.Count, Is.EqualTo(1));
        }

        [Test]
        public void ConsumeFailure_DoesNotExposeReservationTokenInError()
        {
            var transport = new FakeTransport();
            transport.Enqueue(
                500,
                "{\"code\":\"consume_failed\",\"token\":\"reservation-secret\"}");
            Configure(transport, new RotatingKeyProvider("sk_server"));

            var exception = Capture<PlayServGameServerException>(() =>
                PlayServGameServer.ConsumeReservationAsync(
                    "arena",
                    "reservation-secret",
                    "plr_1",
                    "room-1"));

            Assert.That(exception.Message, Does.Not.Contain("reservation-secret"));
            Assert.That(exception.UnifiedError.RawDetails, Does.Not.Contain("reservation-secret"));
        }

        [Test]
        public void ConsumeReservation_UsesEscapedRouteAndAdmissionBody()
        {
            var transport = new FakeTransport();
            transport.Enqueue(
                200,
                "{\"ok\":true,\"room_name\":\"room-1\",\"player_id\":\"plr_1\"}");
            Configure(transport, new RotatingKeyProvider("sk_server"));

            var result = PlayServGameServer.ConsumeReservationAsync(
                    "arena",
                    "ticket/with+symbols",
                    "plr_1",
                    "room-1")
                .GetAwaiter().GetResult();

            Assert.That(result.Ok, Is.True);
            Assert.That(
                transport.Requests[0].RelativePath,
                Is.EqualTo("matchmaking/arena/reservations/ticket%2Fwith%2Bsymbols:consume"));
            Assert.That(transport.Requests[0].JsonBody, Does.Contain("\"player_id\":\"plr_1\""));
            Assert.That(transport.Requests[0].JsonBody, Does.Contain("\"room_name\":\"room-1\""));
        }

        [Test]
        public void PlayerLookup_ReturnsOnlySafeProfileFieldsAndNullFor404()
        {
            var transport = new FakeTransport();
            transport.Enqueue(
                200,
                "{\"id\":\"plr_1\",\"name\":\"Ada\",\"status\":\"active\",\"sso\":[\"steam\"],\"country\":\"UA\",\"joined\":\"2026-08-19\",\"created_at\":\"2026-08-19T00:00:00Z\",\"updated_at\":\"2026-08-19T01:00:00Z\",\"last_ip\":\"127.0.0.1\",\"last_fingerprint_hash\":\"hidden\"}");
            transport.Enqueue(404, "{\"code\":\"player_not_found\"}");
            Configure(transport, new RotatingKeyProvider("sk_server"));

            var player = PlayServGameServer.GetPlayerAsync("plr_1").GetAwaiter().GetResult();
            var missing = PlayServGameServer.GetPlayerAsync("plr_missing").GetAwaiter().GetResult();

            Assert.That(player.Id, Is.EqualTo("plr_1"));
            Assert.That(player.Sso, Is.EquivalentTo(new[] { "steam" }));
            Assert.That(missing, Is.Null);
            Assert.That(typeof(PlayServGameServerPlayerProfile).GetProperty("LastIp"), Is.Null);
            Assert.That(typeof(PlayServGameServerPlayerProfile).GetProperty("LastFingerprintHash"), Is.Null);
            Assert.That(typeof(PlayServGameServerPlayerProfile).GetProperty("Email"), Is.Null);
            Assert.That(typeof(PlayServGameServerPlayerProfile).GetProperty("Moderation"), Is.Null);
        }

        [Test]
        public void ServerRecords_UseServerAclRotatingCredentialAndEtag()
        {
            var transport = new FakeTransport();
            transport.Enqueue(
                200,
                "{\"data\":[{\"entity_id\":\"ent_jobs\",\"name\":\"ServerJob\",\"singleton\":false," +
                "\"read\":\"owner\",\"acl\":{\"client\":{\"read\":false,\"write\":false}," +
                "\"server\":{\"read\":true,\"write\":true}}}]}" );
            transport.Enqueue(
                200,
                "{\"id\":\"rec_1\",\"created_at\":\"2026-08-19T10:00:00Z\"," +
                "\"updated_at\":\"2026-08-19T11:00:00Z\",\"owner\":\"plr_1\",\"state\":\"queued\"}",
                etag: "\"v1\"");
            transport.Enqueue(
                200,
                "{\"id\":\"rec_1\",\"created_at\":\"2026-08-19T10:00:00Z\"," +
                "\"updated_at\":\"2026-08-19T12:00:00Z\",\"owner\":\"plr_1\",\"state\":\"assigned\"}",
                etag: "\"v2\"");
            Configure(
                transport,
                new RotatingKeyProvider("sk_catalogue", "sk_load", "sk_save"));

            var records = PlayServGameServer.Records<ServerJob>();
            var record = records.LoadAsync("rec_1").GetAwaiter().GetResult();
            record.Value.State = "assigned";
            record.SaveAsync().GetAwaiter().GetResult();

            Assert.That(
                records.Capabilities.Server.Read,
                Is.EqualTo(PlayServDataCapabilityState.Allowed));
            Assert.That(
                records.Capabilities.Client.Read,
                Is.EqualTo(PlayServDataCapabilityState.Denied));
            Assert.That(record.ETag, Is.EqualTo("\"v2\""));
            Assert.That(transport.Requests[0].RelativePath, Is.EqualTo("data/tables"));
            Assert.That(transport.Requests[1].RelativePath, Is.EqualTo("data/tables/ent_jobs/records/rec_1"));
            Assert.That(transport.Requests[2].Method, Is.EqualTo("PATCH"));
            Assert.That(transport.Requests[2].IfMatch, Is.EqualTo("\"v1\""));
            Assert.That(transport.Requests[2].ServerKey, Is.EqualTo("sk_save"));
        }

        [Test]
        public void ServerCatalogueAndRecordsResolutionShareMetadataCache()
        {
            var transport = new FakeTransport();
            transport.Enqueue(
                200,
                "{\"data\":[{\"entity_id\":\"ent_jobs\",\"name\":\"ServerJob\"," +
                "\"description\":\"Server queue\",\"singleton\":false,\"row_count\":7," +
                "\"updated_at\":\"2026-08-20T10:00:00Z\",\"read\":\"public\"," +
                "\"acl\":{\"server\":{\"read\":true,\"write\":true}}}]}" );
            Configure(transport, new RotatingKeyProvider("sk_catalogue"));

            var tables = PlayServGameServer.GetTablesAsync().GetAwaiter().GetResult();
            var byName = PlayServGameServer.GetTableAsync("serverjob").GetAwaiter().GetResult();
            var capabilities = PlayServGameServer.Records<ServerJob>()
                .GetCapabilitiesAsync().GetAwaiter().GetResult();

            Assert.That(tables.Count, Is.EqualTo(1));
            Assert.That(byName, Is.SameAs(tables[0]));
            Assert.That(byName.Description, Is.EqualTo("Server queue"));
            Assert.That(byName.RowCount, Is.EqualTo(7));
            Assert.That(capabilities, Is.SameAs(byName.Capabilities));
            Assert.That(transport.Requests, Has.Count.EqualTo(1));
        }

        [Test]
        public void ActingPlayerRecords_SendPlayerJwtOnlyForWrites()
        {
            const string playerJwt = "eyJhbGciOiJSUzI1NiJ9.eyJzdWIiOiJwbHJfMSJ9.signature";
            var transport = new FakeTransport();
            transport.Enqueue(
                200,
                "{\"data\":[{\"entity_id\":\"ent_jobs\",\"name\":\"ServerJob\",\"singleton\":false," +
                "\"acl\":{\"server\":{\"read\":true,\"write\":true}}}]}" );
            transport.Enqueue(
                200,
                "{\"data\":[],\"page\":{\"next_cursor\":null,\"previous_cursor\":null,\"has_more\":false}}" );
            transport.Enqueue(
                201,
                "{\"id\":\"rec_1\",\"created_at\":\"2026-08-19T10:00:00Z\"," +
                "\"updated_at\":\"2026-08-19T10:00:00Z\",\"owner\":\"plr_1\",\"state\":\"queued\"}",
                etag: "\"v1\"");
            transport.Enqueue(
                200,
                "{\"id\":\"rec_1\",\"created_at\":\"2026-08-19T10:00:00Z\"," +
                "\"updated_at\":\"2026-08-19T11:00:00Z\",\"owner\":\"plr_1\",\"state\":\"assigned\"}",
                etag: "\"v2\"");
            transport.Enqueue(204, null);
            Configure(transport, new RotatingKeyProvider(
                "sk_catalogue", "sk_query", "sk_create", "sk_save", "sk_delete"));

            var records = PlayServGameServer.AsPlayer(playerJwt).Records<ServerJob>();
            records.QueryAsync().GetAwaiter().GetResult();
            var record = records.CreateAsync(new ServerJob { State = "queued" }).GetAwaiter().GetResult();
            record.Value.State = "assigned";
            record.SaveAsync().GetAwaiter().GetResult();
            record.DeleteAsync().GetAwaiter().GetResult();

            Assert.That(transport.Requests[0].Headers.ContainsKey("X-Acting-Player"), Is.False);
            Assert.That(transport.Requests[1].Headers.ContainsKey("X-Acting-Player"), Is.False);
            Assert.That(transport.Requests[2].Headers["X-Acting-Player"], Is.EqualTo(playerJwt));
            Assert.That(transport.Requests[3].Headers["X-Acting-Player"], Is.EqualTo(playerJwt));
            Assert.That(transport.Requests[4].Headers["X-Acting-Player"], Is.EqualTo(playerJwt));
        }

        [TestCase("")]
        [TestCase("not-a-jwt")]
        [TestCase("Bearer aaa.bbb.ccc")]
        [TestCase("aaa.bbb.ccc\nsecret")]
        public void ActingPlayerRecords_RejectInvalidJwtBeforeCredentialOrIo(string playerJwt)
        {
            var transport = new FakeTransport();
            var provider = new RotatingKeyProvider("sk_server");
            Configure(transport, provider);

            Assert.Throws<ArgumentException>(() => PlayServGameServer.AsPlayer(playerJwt));

            Assert.That(provider.CallCount, Is.Zero);
            Assert.That(transport.Requests, Is.Empty);
        }

        [Test]
        public void ServerCode_ForwardsFunctionOptionsWithoutPublicClientCredential()
        {
            var transport = new FakeTransport();
            transport.Enqueue(
                200,
                "{\"room\":\"eu-17\"}",
                contentType: "application/json");
            Configure(transport, new RotatingKeyProvider("sk_function"));

            var result = PlayServGameServer.Code.CallAsync<AllocateResponse>(
                    "allocate-room",
                    new { region = "eu" },
                    new PlayServFunctionCallOptions
                    {
                        Version = "stable",
                        Query = new Dictionary<string, string> { ["mode"] = "ranked" },
                        TimeoutSeconds = 17
                    })
                .GetAwaiter().GetResult();

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.Room, Is.EqualTo("eu-17"));
            Assert.That(transport.Requests[0].RelativePath, Is.EqualTo("fn/allocate-room?mode=ranked"));
            Assert.That(transport.Requests[0].ServerKey, Is.EqualTo("sk_function"));
            Assert.That(transport.Requests[0].FunctionVersion, Is.EqualTo("stable"));
            Assert.That(transport.Requests[0].Timeout, Is.EqualTo(TimeSpan.FromSeconds(17)));
        }

        [Test]
        public void ServerAnalytics_UsesBoundedQueueAndPerEventPlayerAttribution()
        {
            var transport = new FakeTransport();
            transport.Enqueue(202, "{\"accepted\":2}");
            Configure(transport, new RotatingKeyProvider("sk_analytics"));

            PlayServGameServer.Analytics.Track(
                "round_started",
                new Dictionary<string, object> { ["mode"] = "ranked" },
                playerId: "plr_1");
            PlayServGameServer.Analytics.Track(
                "round_started",
                new Dictionary<string, object> { ["mode"] = "casual" },
                playerId: "plr_2");

            Assert.That(PlayServGameServer.Analytics.PendingEventCount, Is.EqualTo(2));
            PlayServGameServer.Analytics.FlushAsync().GetAwaiter().GetResult();

            Assert.That(PlayServGameServer.Analytics.PendingEventCount, Is.Zero);
            Assert.That(transport.Requests, Has.Count.EqualTo(1));
            Assert.That(transport.Requests[0].Method, Is.EqualTo("POST"));
            Assert.That(transport.Requests[0].RelativePath, Is.EqualTo("analytics/events"));
            Assert.That(transport.Requests[0].ServerKey, Is.EqualTo("sk_analytics"));
            Assert.That(transport.Requests[0].JsonBody, Does.Contain("\"user_id\":\"plr_1\""));
            Assert.That(transport.Requests[0].JsonBody, Does.Contain("\"user_id\":\"plr_2\""));
            Assert.That(transport.Requests[0].JsonBody, Does.Contain("\"mode\":\"ranked\""));
        }

        [Test]
        public void ServerAnalytics_FailedFlushRetainsEventsForRetry()
        {
            var transport = new FakeTransport();
            transport.Enqueue(503, "{\"code\":\"analytics_busy\",\"retryable\":true}");
            transport.Enqueue(202, "{\"accepted\":1}");
            Configure(transport, new RotatingKeyProvider("sk_first", "sk_second"));
            PlayServGameServer.Analytics.Track("room_tick", playerId: "plr_1");

            var failure = Capture<PlayServGameServerException>(() =>
                PlayServGameServer.Analytics.FlushAsync());

            Assert.That(failure.UnifiedError.SourceCode, Is.EqualTo("analytics_busy"));
            Assert.That(failure.UnifiedError.Retryable, Is.True);
            Assert.That(PlayServGameServer.Analytics.PendingEventCount, Is.EqualTo(1));

            PlayServGameServer.Analytics.FlushAsync().GetAwaiter().GetResult();
            Assert.That(PlayServGameServer.Analytics.PendingEventCount, Is.Zero);
            Assert.That(transport.Requests[0].ServerKey, Is.EqualTo("sk_first"));
            Assert.That(transport.Requests[1].ServerKey, Is.EqualTo("sk_second"));
        }

        [Test]
        public void ServerAnalytics_InvalidPlayerIdIsRejectedBeforeIo()
        {
            var transport = new FakeTransport();
            var provider = new RotatingKeyProvider("sk_server");
            Configure(transport, provider);

            Assert.Throws<ArgumentException>(() =>
                PlayServGameServer.Analytics.Track("event", playerId: "plr/invalid"));

            Assert.That(provider.CallCount, Is.Zero);
            Assert.That(transport.Requests, Is.Empty);
        }

        [Test]
        public void ServerCommerce_ReusesTypedPagesAndRotatingServerBearer()
        {
            var transport = new FakeTransport();
            transport.Enqueue(
                200,
                "{\"data\":[{\"id\":\"itm_1\",\"sku\":\"coins\",\"name\":\"Coins\",\"status\":\"active\"}]," +
                "\"page\":{\"cursor_next\":\"next-1\",\"has_more\":true},\"total_estimate\":3}");
            transport.Enqueue(
                200,
                "{\"id\":\"sf_1\",\"name\":\"Main\",\"status\":\"active\",\"items\":[]}");
            Configure(transport, new RotatingKeyProvider("sk_catalog", "sk_storefront"));

            var page = PlayServGameServer.Catalog.ListAsync(
                    new PlayServCatalogQuery
                    {
                        Status = "active",
                        Search = "coin pack",
                        Sort = "name",
                        Cursor = "cur/1",
                        Limit = 25
                    })
                .GetAwaiter().GetResult();
            var storefront = PlayServGameServer.Storefronts.GetAsync("sf_1")
                .GetAwaiter().GetResult();

            Assert.That(page.Items.Count, Is.EqualTo(1));
            Assert.That(page.Page.CursorNext, Is.EqualTo("next-1"));
            Assert.That(page.TotalEstimate, Is.EqualTo(3));
            Assert.That(storefront.Name, Is.EqualTo("Main"));
            Assert.That(
                transport.Requests[0].RelativePath,
                Is.EqualTo("catalog/items?status=active&q=coin%20pack&sort=name&cursor=cur%2F1&limit=25"));
            Assert.That(transport.Requests[0].ServerKey, Is.EqualTo("sk_catalog"));
            Assert.That(transport.Requests[1].RelativePath, Is.EqualTo("storefronts/sf_1"));
            Assert.That(transport.Requests[1].ServerKey, Is.EqualTo("sk_storefront"));
        }

        [Test]
        public void Shutdown_PreservesRoomCloseResultsWhenAnalyticsFlushFails()
        {
            var transport = new FakeTransport();
            transport.Enqueue(200, ActiveUpsert(created: true));
            transport.Enqueue(503, "{\"code\":\"analytics_unavailable\"}");
            transport.Enqueue(204, null);
            Configure(transport, new RotatingKeyProvider(
                "sk_upsert",
                "sk_analytics",
                "sk_close"));
            var room = PlayServGameServer.StartRoomAsync(
                    new PlayServStartRoomRequest(
                        "arena",
                        new PlayServGameRoomSnapshot("room-1", 0, 8)))
                .GetAwaiter().GetResult();
            PlayServGameServer.Analytics.Track("server_stopping");

            var shutdown = PlayServGameServer.ShutdownAsync().GetAwaiter().GetResult();

            Assert.That(shutdown.IsSuccess, Is.False);
            Assert.That(shutdown.AnalyticsError.SourceCode, Is.EqualTo("analytics_unavailable"));
            Assert.That(shutdown.Rooms, Has.Count.EqualTo(1));
            Assert.That(shutdown.Rooms[0].IsSuccess, Is.True);
            Assert.That(room.State, Is.EqualTo(PlayServGameRoomState.Closed));
            Assert.That(transport.Requests[1].RelativePath, Is.EqualTo("analytics/events"));
            Assert.That(transport.Requests[2].RelativePath, Is.EqualTo("matchmaking/arena/rooms/room-1:close"));
        }

        private static void Configure(
            FakeTransport transport,
            IPlayServServerKeyProvider provider)
        {
            PlayServGameServer.ConfigureForTesting(
                new PlayServGameServerOptions
                {
                    BackendServerAddress = "https://api.playserv.test/ws",
                    ServerKeyProvider = provider,
                    HeartbeatInterval = TimeSpan.FromSeconds(5),
                    HttpTimeout = TimeSpan.FromSeconds(10)
                },
                transport,
                () => new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero),
                (_, ct) => Task.Delay(Timeout.Infinite, ct));
        }

        private static string ActiveUpsert(bool created)
        {
            return "{\"created\":" + (created ? "true" : "false") +
                   ",\"placement\":{\"state\":\"active\",\"open\":true,\"draining\":false,\"open_refused\":false}}";
        }

        private static TException Capture<TException>(Func<Task> action)
            where TException : Exception
        {
            try
            {
                action().GetAwaiter().GetResult();
            }
            catch (TException exception)
            {
                return exception;
            }

            Assert.Fail("Expected exception " + typeof(TException).Name + ".");
            return null;
        }

        private sealed class RotatingKeyProvider : IPlayServServerKeyProvider
        {
            private readonly Queue<string> _keys;
            private string _last;

            internal RotatingKeyProvider(params string[] keys)
            {
                _keys = new Queue<string>(keys);
            }

            internal int CallCount { get; private set; }

            public Task<string> GetServerKeyAsync(CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CallCount++;
                if (_keys.Count > 0)
                    _last = _keys.Dequeue();
                return Task.FromResult(_last);
            }
        }

        private sealed class FakeTransport : IPlayServGameServerTransport
        {
            private readonly Queue<Func<PlayServGameServerHttpResponse>> _responses =
                new Queue<Func<PlayServGameServerHttpResponse>>();

            internal List<RequestSnapshot> Requests { get; } = new List<RequestSnapshot>();

            internal void Enqueue(
                int status,
                string body,
                string etag = null,
                string contentType = null)
            {
                _responses.Enqueue(() => new PlayServGameServerHttpResponse
                {
                    StatusCode = status,
                    Body = body,
                    ETag = etag,
                    ContentType = contentType
                });
            }

            internal void EnqueueFailure(bool isTimeout)
            {
                _responses.Enqueue(() => throw new PlayServGameServerTransportFailure(
                    isTimeout ? "timeout" : "network",
                    isTimeout));
            }

            public Task<PlayServGameServerHttpResponse> SendAsync(
                PlayServGameServerHttpRequest request,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Requests.Add(new RequestSnapshot(
                    request.Method,
                    request.RelativePath,
                    request.JsonBody,
                    request.ServerKey,
                    request.Timeout,
                    request.IfMatch,
                    request.IdempotencyKey,
                    request.FunctionVersion,
                    request.Headers));
                if (_responses.Count == 0)
                    throw new InvalidOperationException("No fake response was queued.");
                return Task.FromResult(_responses.Dequeue()());
            }
        }

        internal sealed class RequestSnapshot
        {
            internal RequestSnapshot(
                string method,
                string relativePath,
                string jsonBody,
                string serverKey,
                TimeSpan timeout,
                string ifMatch,
                string idempotencyKey,
                string functionVersion,
                IReadOnlyDictionary<string, string> headers)
            {
                Method = method;
                RelativePath = relativePath;
                JsonBody = jsonBody;
                ServerKey = serverKey;
                Timeout = timeout;
                IfMatch = ifMatch;
                IdempotencyKey = idempotencyKey;
                FunctionVersion = functionVersion;
                Headers = headers == null
                    ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase);
            }

            internal string Method { get; }
            internal string RelativePath { get; }
            internal string JsonBody { get; }
            internal string ServerKey { get; }
            internal TimeSpan Timeout { get; }
            internal string IfMatch { get; }
            internal string IdempotencyKey { get; }
            internal string FunctionVersion { get; }
            internal IReadOnlyDictionary<string, string> Headers { get; }
        }

        private sealed class ServerJob
        {
            public string State { get; set; }
        }

        private sealed class AllocateResponse
        {
            public string Room { get; set; }
        }
    }
}
