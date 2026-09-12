using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.Data;
using Playserv.DataSubscription;
using Playserv.DataSubscription.Requests;
using Playserv.DataSubscription.Responses;
using Playserv.Http.Interfaces;
using Playserv.Modules;
using Playserv.Proxy.Common;
using Playserv.Proxy.Logging;
using Playserv.Runtime.Abstractions;
using Playserv.Schema;
using Playserv.Serialization;
using Playserv.Wrapper;

namespace Playserv.Tests.Runtime
{
    public sealed partial class PlayServTypedRecordsTests
    {
        [Test]
        public void PublicCatalogueMetadataLookupAndRecordsResolutionShareOneSnapshot()
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueCatalogueJson(
                "{\"data\":[{\"entity_id\":\"ent_inventory\",\"name\":\"InventoryItem\"," +
                "\"description\":\"Player inventory\",\"singleton\":false,\"row_count\":42," +
                "\"updated_at\":\"2026-08-20T10:00:00Z\",\"read\":\"owner\"," +
                "\"acl\":{\"client\":{\"read\":true,\"write\":false}," +
                "\"server\":{\"read\":true,\"write\":true}," +
                "\"backend\":{\"read\":true,\"write\":true}}}]}" );
            var client = new PlayServRecordsClient(Settings(), fake, new NewtonsoftJsonCodec());

            var tables = client.GetTablesAsync(false, default).GetAwaiter().GetResult();
            var byId = client.GetTableAsync("ent_inventory", default).GetAwaiter().GetResult();
            var byName = client.GetTableAsync("inventoryitem", default).GetAwaiter().GetResult();
            var records = new PlayServRecordSet<InventoryItem>(client);
            var capabilities = records.GetCapabilitiesAsync().GetAwaiter().GetResult();

            Assert.That(tables.Count, Is.EqualTo(1));
            Assert.That(byId, Is.SameAs(tables[0]));
            Assert.That(byName, Is.SameAs(tables[0]));
            Assert.That(byId.Description, Is.EqualTo("Player inventory"));
            Assert.That(byId.RowCount, Is.EqualTo(42));
            Assert.That(byId.UpdatedAt, Is.EqualTo(DateTimeOffset.Parse("2026-08-20T10:00:00Z")));
            Assert.That(byId.ReadPolicy, Is.EqualTo("owner"));
            Assert.That(capabilities, Is.SameAs(byId.Capabilities));
            Assert.That(fake.AllRequests, Has.Count.EqualTo(1));
        }

        [Test]
        public void ForcedCatalogueRefreshAtomicallyReplacesSnapshotAndMissingIsTyped()
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueCatalogueJson(
                "{\"data\":[{\"entity_id\":\"ent_inventory\",\"name\":\"InventoryItem\"," +
                "\"description\":\"old\",\"singleton\":false,\"row_count\":1}]}" );
            fake.EnqueueCatalogueJson(
                "{\"data\":[{\"entity_id\":\"ent_inventory\",\"name\":\"InventoryItem\"," +
                "\"description\":\"new\",\"singleton\":false,\"row_count\":2}]}" );
            var client = new PlayServRecordsClient(Settings(), fake, new NewtonsoftJsonCodec());

            var oldSnapshot = client.GetTablesAsync(false, default).GetAwaiter().GetResult();
            var newSnapshot = client.GetTablesAsync(true, default).GetAwaiter().GetResult();
            var missing = Assert.Throws<PlayServDataTableNotFoundException>(() =>
                client.GetTableAsync("missing", default).GetAwaiter().GetResult());

            Assert.That(oldSnapshot[0].Description, Is.EqualTo("old"));
            Assert.That(oldSnapshot[0].RowCount, Is.EqualTo(1));
            Assert.That(newSnapshot[0].Description, Is.EqualTo("new"));
            Assert.That(newSnapshot[0].RowCount, Is.EqualTo(2));
            Assert.That(missing.IdOrName, Is.EqualTo("missing"));
            Assert.That(missing.UnifiedError.Code, Is.EqualTo(PlayServErrorCode.NotFound));
            Assert.That(fake.AllRequests, Has.Count.EqualTo(2));
        }

        [Test]
        public void Entity_resolution_uses_type_name_and_create_keeps_server_minted_id()
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueJson(200,
                "{\"data\":[{\"entity_id\":\"ent_inventory\",\"name\":\"InventoryItem\",\"singleton\":false}]}");
            fake.EnqueueJson(201, RecordJson("rec_server", "sword", "Sword"), "\"a1\"");
            var set = CreateSet<InventoryItem>(fake, explicitEntityId: null);

            var record = set.CreateAsync(new InventoryItem
            {
                Id = "client-id-must-not-be-sent",
                Code = "sword",
                DisplayName = "Sword",
                Ignored = "local"
            }).GetAwaiter().GetResult();

            Assert.That(record.Id, Is.EqualTo("rec_server"));
            Assert.That(record.EntityId, Is.EqualTo("ent_inventory"));
            Assert.That(fake.Requests[0].RelativePath, Is.EqualTo("data/tables"));
            Assert.That(fake.Requests[1].JsonBody, Does.Contain("\"code_wire\":\"sword\""));
            Assert.That(fake.Requests[1].JsonBody, Does.Contain("\"display_name\":\"Sword\""));
            Assert.That(fake.Requests[1].JsonBody, Does.Not.Contain("client-id-must-not-be-sent"));
            Assert.That(fake.Requests[1].JsonBody, Does.Not.Contain("Ignored"));
            Assert.That(fake.Requests[1].IdempotencyKey, Is.Not.Empty);
        }

        [Test]
        public void Bulk_create_uses_atomic_endpoint_and_preserves_server_id_order()
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueJson(200, "{\"ids\":[\"rec_first\",\"rec_second\"],\"created\":2}");
            var set = CreateSet<InventoryItem>(fake, "ent_inventory");

            var result = set.BulkCreateAsync(
                new[]
                {
                    new InventoryItem { Code = "sword", DisplayName = "Sword" },
                    new InventoryItem { Code = "shield", DisplayName = "Shield" }
                },
                new Dictionary<string, object> { ["rarity"] = "common" })
                .GetAwaiter().GetResult();

            Assert.That(result.CreatedCount, Is.EqualTo(2));
            Assert.That(result.RecordIds, Is.EqualTo(new[] { "rec_first", "rec_second" }));
            Assert.That(fake.Requests, Has.Count.EqualTo(1));
            Assert.That(fake.Requests[0].Method, Is.EqualTo("POST"));
            Assert.That(fake.Requests[0].RelativePath,
                Is.EqualTo("data/tables/ent_inventory/records:bulk-create"));
            Assert.That(fake.Requests[0].JsonBody, Does.Contain("\"defaults\":{\"rarity\":\"common\"}"));
            Assert.That(fake.Requests[0].JsonBody, Does.Contain("\"records\":["));
            Assert.That(fake.Requests[0].IdempotencyKey, Is.Not.Empty);
        }

        [Test]
        public void Bulk_create_rejects_invalid_batch_before_io()
        {
            var fake = new FakeDataHttpClient();
            var set = CreateSet<InventoryItem>(fake, "ent_inventory");

            CaptureExceptionAsync<ArgumentOutOfRangeException>(() =>
                set.BulkCreateAsync(Array.Empty<InventoryItem>())).GetAwaiter().GetResult();

            Assert.That(fake.Requests, Is.Empty);
        }

        [Test]
        public void Natural_key_load_uses_indexed_endpoint_with_projection_and_expand()
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueJson(200, RecordJson("rec_server", "two words", "Sword"), "\"a1\"");
            var set = CreateSet<InventoryItem>(fake, "ent_inventory");

            var record = set.LoadByNaturalKeyAsync(
                PlayServNaturalKey<InventoryItem>.For(item => item.Code, "two words"),
                new PlayServLoadOptions
                {
                    Fields = new[] { "code_wire" },
                    Expand = new[] { "owner_relation" }
                })
                .GetAwaiter().GetResult();

            Assert.That(record.Id, Is.EqualTo("rec_server"));
            Assert.That(record.IsPartial, Is.True);
            Assert.That(fake.Requests[0].Method, Is.EqualTo("GET"));
            Assert.That(fake.Requests[0].RelativePath, Is.EqualTo(
                "data/tables/ent_inventory/records:by-natural-key?field=code_wire&value=two%20words&fields=code_wire&expand=owner_relation"));
        }

        [Test]
        public void Delete_matching_uses_one_atomic_filtered_request()
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueJson(200, "{\"ids\":[\"rec_first\",\"rec_second\"],\"deleted\":2}");
            var set = CreateSet<InventoryItem>(fake, "ent_inventory");
            var query = new PlayServRecordQuery<InventoryItem>()
                .Where(item => item.Code, PlayServQueryOperator.Eq, "obsolete");

            var result = set.DeleteMatchingAsync(
                query,
                PlayServDeleteAllConfirmation.MatchingRecords).GetAwaiter().GetResult();

            Assert.That(result.DeletedCount, Is.EqualTo(2));
            Assert.That(result.RecordIds, Is.EqualTo(new[] { "rec_first", "rec_second" }));
            Assert.That(fake.Requests, Has.Count.EqualTo(1));
            Assert.That(fake.Requests[0].Method, Is.EqualTo("POST"));
            Assert.That(fake.Requests[0].RelativePath,
                Is.EqualTo("data/tables/ent_inventory/records:delete-by-filter"));
            Assert.That(fake.Requests[0].JsonBody, Does.Contain("\"field\":\"code_wire\""));
            Assert.That(fake.Requests[0].JsonBody, Does.Contain("\"all\":null"));
        }

        [Test]
        public void Delete_matching_requires_explicit_all_confirmation_before_io()
        {
            var fake = new FakeDataHttpClient();
            var set = CreateSet<InventoryItem>(fake, "ent_inventory");

            CaptureExceptionAsync<InvalidOperationException>(() => set.DeleteMatchingAsync(
                null,
                PlayServDeleteAllConfirmation.MatchingRecords)).GetAwaiter().GetResult();

            Assert.That(fake.Requests, Is.Empty);
        }

        [Test]
        public void Capabilities_parse_all_subjects_and_read_policy_for_explicit_entity()
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueCatalogueJson(AclCatalogueJson(
                "ent_inventory",
                "InventoryItem",
                false,
                "owner",
                true,
                false,
                true,
                true,
                false,
                true));
            var set = CreateSet<InventoryItem>(fake, "ent_inventory");

            Assert.That(set.Capabilities.IsKnown, Is.False);
            var capabilities = set.GetCapabilitiesAsync().GetAwaiter().GetResult();

            Assert.That(capabilities, Is.SameAs(set.Capabilities));
            Assert.That(capabilities.IsKnown, Is.True);
            Assert.That(capabilities.CanRead, Is.True);
            Assert.That(capabilities.CanWrite, Is.False);
            Assert.That(capabilities.ReadPolicy, Is.EqualTo("owner"));
            Assert.That(capabilities.Client.Read, Is.EqualTo(PlayServDataCapabilityState.Allowed));
            Assert.That(capabilities.Client.Write, Is.EqualTo(PlayServDataCapabilityState.Denied));
            Assert.That(capabilities.Server.Read, Is.EqualTo(PlayServDataCapabilityState.Allowed));
            Assert.That(capabilities.Server.Write, Is.EqualTo(PlayServDataCapabilityState.Allowed));
            Assert.That(capabilities.Backend.Read, Is.EqualTo(PlayServDataCapabilityState.Denied));
            Assert.That(capabilities.Backend.Write, Is.EqualTo(PlayServDataCapabilityState.Allowed));
            Assert.That(fake.AllRequests.Count, Is.EqualTo(1));
            Assert.That(fake.AllRequests[0].RelativePath, Is.EqualTo("data/tables"));
        }

        [Test]
        public void Missing_acl_is_unknown_and_does_not_block_the_backend_request()
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueJson(200, RecordJson("rec_1", "sword", "Sword"), "\"a1\"");
            var set = CreateSet<InventoryItem>(fake, "ent_inventory");

            var record = set.LoadAsync("rec_1").GetAwaiter().GetResult();

            Assert.That(record.Id, Is.EqualTo("rec_1"));
            Assert.That(set.Capabilities.IsKnown, Is.False);
            Assert.That(set.Capabilities.Client.Read, Is.EqualTo(PlayServDataCapabilityState.Unknown));
            Assert.That(fake.Requests.Count, Is.EqualTo(1));
            Assert.That(fake.Requests[0].Method, Is.EqualTo("GET"));
        }

        [Test]
        public void Denied_read_refreshes_once_then_fails_before_record_io()
        {
            var fake = new FakeDataHttpClient();
            var denied = AclCatalogueJson(
                "ent_inventory",
                "InventoryItem",
                false,
                "owner",
                false,
                false);
            fake.EnqueueCatalogueJson(denied);
            fake.EnqueueCatalogueJson(denied);
            var set = CreateSet<InventoryItem>(fake, "ent_inventory");

            var exception = CaptureExceptionAsync<PlayServDataAccessDeniedException>(() =>
                set.LoadAsync("rec_1")).GetAwaiter().GetResult();

            Assert.That(exception.EntityId, Is.EqualTo("ent_inventory"));
            Assert.That(exception.Operation, Is.EqualTo(PlayServDataAccessOperation.Read));
            Assert.That(exception.WasRejectedLocally, Is.True);
            Assert.That(exception.BackendCode, Is.EqualTo("table_read_forbidden"));
            Assert.That(exception.UnifiedError.Code, Is.EqualTo(PlayServErrorCode.Forbidden));
            Assert.That(exception.UnifiedError.SourceCode, Is.EqualTo("table_read_forbidden"));
            Assert.That(fake.Requests, Is.Empty);
            Assert.That(fake.AllRequests.Count, Is.EqualTo(2));
        }

        [Test]
        public void Denied_write_prevents_loaded_record_save_before_patch_io()
        {
            var fake = new FakeDataHttpClient();
            var readOnly = AclCatalogueJson(
                "ent_inventory",
                "InventoryItem",
                false,
                "owner",
                true,
                false);
            fake.EnqueueCatalogueJson(readOnly);
            fake.EnqueueCatalogueJson(readOnly);
            fake.EnqueueJson(200, RecordJson("rec_1", "sword", "Sword"), "\"a1\"");
            var set = CreateSet<InventoryItem>(fake, "ent_inventory");
            var record = set.LoadAsync("rec_1").GetAwaiter().GetResult();
            record.Value.DisplayName = "Renamed";

            var exception = CaptureExceptionAsync<PlayServDataAccessDeniedException>(() =>
                record.SaveAsync()).GetAwaiter().GetResult();

            Assert.That(exception.Operation, Is.EqualTo(PlayServDataAccessOperation.Write));
            Assert.That(exception.WasRejectedLocally, Is.True);
            Assert.That(fake.Requests.Count, Is.EqualTo(1));
            Assert.That(fake.Requests[0].Method, Is.EqualTo("GET"));
            Assert.That(fake.AllRequests.Count, Is.EqualTo(3));
        }

        [Test]
        public void Denied_write_prevents_create_and_delete_mutations()
        {
            var createFake = new FakeDataHttpClient();
            var readOnly = AclCatalogueJson(
                "ent_inventory", "InventoryItem", false, "owner", true, false);
            createFake.EnqueueCatalogueJson(readOnly);
            createFake.EnqueueCatalogueJson(readOnly);
            var createSet = CreateSet<InventoryItem>(createFake, "ent_inventory");

            CaptureExceptionAsync<PlayServDataAccessDeniedException>(() =>
                createSet.CreateAsync(new InventoryItem { Code = "sword" }))
                .GetAwaiter().GetResult();
            Assert.That(createFake.Requests, Is.Empty);

            var deleteFake = new FakeDataHttpClient();
            deleteFake.EnqueueCatalogueJson(readOnly);
            deleteFake.EnqueueCatalogueJson(readOnly);
            deleteFake.EnqueueJson(200, RecordJson("rec_1", "sword", "Sword"), "\"a1\"");
            var deleteSet = CreateSet<InventoryItem>(deleteFake, "ent_inventory");
            var record = deleteSet.LoadAsync("rec_1").GetAwaiter().GetResult();

            CaptureExceptionAsync<PlayServDataAccessDeniedException>(() => record.DeleteAsync())
                .GetAwaiter().GetResult();
            Assert.That(record.IsDeleted, Is.False);
            Assert.That(deleteFake.Requests.Count, Is.EqualTo(1));
            Assert.That(deleteFake.Requests[0].Method, Is.EqualTo("GET"));
        }

        [Test]
        public void Load_or_create_checks_write_capability_after_miss_before_factory()
        {
            var fake = new FakeDataHttpClient();
            var readOnly = AclCatalogueJson(
                "ent_inventory", "InventoryItem", false, "owner", true, false);
            fake.EnqueueCatalogueJson(readOnly);
            fake.EnqueueCatalogueJson(readOnly);
            fake.EnqueueException(HttpError(404, "not_found", "{}"));
            var set = CreateSet<InventoryItem>(fake, "ent_inventory");
            var factoryCalls = 0;

            CaptureExceptionAsync<PlayServDataAccessDeniedException>(() =>
                set.LoadOrCreateAsync(
                    PlayServNaturalKey<InventoryItem>.For(item => item.Code, "sword"),
                    () =>
                    {
                        factoryCalls++;
                        return new InventoryItem { Code = "sword" };
                    })).GetAwaiter().GetResult();

            Assert.That(factoryCalls, Is.Zero);
            Assert.That(fake.Requests.Count, Is.EqualTo(1));
            Assert.That(fake.Requests[0].RelativePath, Does.Contain("/records:by-natural-key?"));
        }

        [Test]
        public void Denied_singleton_write_stops_save_before_patch_io()
        {
            var fake = new FakeDataHttpClient();
            var readOnly = AclCatalogueJson(
                "ent_config", "GameConfig", true, "public", true, false);
            fake.EnqueueCatalogueJson(readOnly);
            fake.EnqueueCatalogueJson(readOnly);
            fake.EnqueueJson(200, SingletonJson("Welcome"), "\"b1\"");
            var set = CreateSet<GameConfig>(fake, "ent_config");
            var singleton = set.GetSingletonAsync().GetAwaiter().GetResult();
            singleton.Value.Message = "Hello";

            CaptureExceptionAsync<PlayServDataAccessDeniedException>(() => singleton.SaveAsync())
                .GetAwaiter().GetResult();

            Assert.That(fake.Requests.Count, Is.EqualTo(1));
            Assert.That(fake.Requests[0].Method, Is.EqualTo("GET"));
        }

        [Test]
        public void Denied_read_prevents_query_singleton_and_subscription_io()
        {
            var queryFake = new FakeDataHttpClient();
            var tableDenied = AclCatalogueJson(
                "ent_inventory", "InventoryItem", false, "owner", false, false);
            queryFake.EnqueueCatalogueJson(tableDenied);
            queryFake.EnqueueCatalogueJson(tableDenied);
            var querySet = CreateSet<InventoryItem>(queryFake, "ent_inventory");
            CaptureExceptionAsync<PlayServDataAccessDeniedException>(() => querySet.QueryAsync())
                .GetAwaiter().GetResult();
            Assert.That(queryFake.Requests, Is.Empty);

            var singletonFake = new FakeDataHttpClient();
            var singletonDenied = AclCatalogueJson(
                "ent_config", "GameConfig", true, "owner", false, false);
            singletonFake.EnqueueCatalogueJson(singletonDenied);
            singletonFake.EnqueueCatalogueJson(singletonDenied);
            var singletonSet = CreateSet<GameConfig>(singletonFake, "ent_config");
            CaptureExceptionAsync<PlayServDataAccessDeniedException>(() =>
                singletonSet.GetSingletonAsync()).GetAwaiter().GetResult();
            Assert.That(singletonFake.Requests, Is.Empty);

            var subscriptionFake = new FakeDataHttpClient();
            subscriptionFake.EnqueueCatalogueJson(tableDenied);
            subscriptionFake.EnqueueCatalogueJson(tableDenied);
            var opened = false;
            var subscriptionSet = CreateSet<InventoryItem>(
                subscriptionFake,
                "ent_inventory",
                subscribeAsync: (query, variables, ct) =>
                {
                    opened = true;
                    return Task.FromResult<ISharedCollection<InventoryItem>>(
                        new FakeSharedCollection<InventoryItem>());
                });
            CaptureExceptionAsync<PlayServDataAccessDeniedException>(() =>
                subscriptionSet.SubscribeAsync()).GetAwaiter().GetResult();
            Assert.That(opened, Is.False);
            Assert.That(subscriptionFake.Requests, Is.Empty);
        }

        [Test]
        public void Denied_read_becoming_allowed_after_refresh_continues_operation()
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueCatalogueJson(AclCatalogueJson(
                "ent_inventory", "InventoryItem", false, "owner", false, false));
            fake.EnqueueCatalogueJson(AclCatalogueJson(
                "ent_inventory", "InventoryItem", false, "owner", true, false));
            fake.EnqueueJson(200, RecordJson("rec_1", "sword", "Sword"), "\"a1\"");
            var set = CreateSet<InventoryItem>(fake, "ent_inventory");

            var record = set.LoadAsync("rec_1").GetAwaiter().GetResult();

            Assert.That(record.Id, Is.EqualTo("rec_1"));
            Assert.That(set.Capabilities.CanRead, Is.True);
            Assert.That(fake.AllRequests.Count, Is.EqualTo(3));
            Assert.That(fake.Requests.Count, Is.EqualTo(1));
        }

        [Test]
        public void Backend_table_forbidden_maps_to_typed_non_local_exception()
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueCatalogueJson(AclCatalogueJson(
                "ent_inventory", "InventoryItem", false, "public", true, true));
            fake.EnqueueException(HttpError(
                403,
                "table_read_forbidden",
                "{\"code\":\"table_read_forbidden\",\"detail\":\"read disabled\"}"));
            var set = CreateSet<InventoryItem>(fake, "ent_inventory");

            var exception = CaptureExceptionAsync<PlayServDataAccessDeniedException>(() =>
                set.LoadAsync("rec_1")).GetAwaiter().GetResult();

            Assert.That(exception.EntityId, Is.EqualTo("ent_inventory"));
            Assert.That(exception.Operation, Is.EqualTo(PlayServDataAccessOperation.Read));
            Assert.That(exception.WasRejectedLocally, Is.False);
            Assert.That(exception.UnifiedError.Code, Is.EqualTo(PlayServErrorCode.Forbidden));
            Assert.That(exception.UnifiedError.SourceCode, Is.EqualTo("table_read_forbidden"));
        }

        [Test]
        public void Backend_table_write_forbidden_preserves_exact_source_code()
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueCatalogueJson(AclCatalogueJson(
                "ent_inventory", "InventoryItem", false, "public", true, true));
            fake.EnqueueException(HttpError(
                403,
                "table_write_forbidden",
                "{\"code\":\"table_write_forbidden\",\"detail\":\"write disabled\"}"));
            var set = CreateSet<InventoryItem>(fake, "ent_inventory");

            var exception = CaptureExceptionAsync<PlayServDataAccessDeniedException>(() =>
                set.CreateAsync(new InventoryItem { Code = "sword" }))
                .GetAwaiter().GetResult();

            Assert.That(exception.Operation, Is.EqualTo(PlayServDataAccessOperation.Write));
            Assert.That(exception.WasRejectedLocally, Is.False);
            Assert.That(exception.BackendCode, Is.EqualTo("table_write_forbidden"));
            Assert.That(exception.UnifiedError.SourceCode, Is.EqualTo("table_write_forbidden"));
        }

        [Test]
        public void Concurrent_capability_lookups_share_one_catalogue_request_and_refresh_reloads_it()
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueCatalogueJson(AclCatalogueJson(
                "ent_inventory", "InventoryItem", false, "owner", true, false));
            fake.EnqueueCatalogueJson(AclCatalogueJson(
                "ent_inventory", "InventoryItem", false, "public", true, true));
            var set = CreateSet<InventoryItem>(fake, "ent_inventory");

            Task.WhenAll(set.GetCapabilitiesAsync(), set.GetCapabilitiesAsync())
                .GetAwaiter().GetResult();
            Assert.That(fake.AllRequests.Count, Is.EqualTo(1));

            var refreshed = set.RefreshCapabilitiesAsync().GetAwaiter().GetResult();
            Assert.That(refreshed.CanWrite, Is.True);
            Assert.That(refreshed.ReadPolicy, Is.EqualTo("public"));
            Assert.That(fake.AllRequests.Count, Is.EqualTo(2));
        }

        [Test]
        public void Save_sends_top_level_merge_patch_with_explicit_null_and_rotates_etag()
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueJson(200, RecordJson("rec_1", "sword", "Sword"), "\"a1\"");
            fake.EnqueueJson(200, RecordJson("rec_1", "sword", null, "2026-08-17T01:00:00Z"), "\"a2\"");
            var set = CreateSet<InventoryItem>(fake, "ent_inventory");
            var record = set.LoadAsync("rec_1").GetAwaiter().GetResult();

            record.Value.DisplayName = null;
            Assert.That(record.HasPendingChanges, Is.True);
            record.SaveAsync().GetAwaiter().GetResult();

            Assert.That(fake.Requests[1].Method, Is.EqualTo("PATCH"));
            Assert.That(fake.Requests[1].IfMatch, Is.EqualTo("\"a1\""));
            Assert.That(fake.Requests[1].JsonBody, Is.EqualTo("{\"display_name\":null}"));
            Assert.That(record.ETag, Is.EqualTo("\"a2\""));
            Assert.That(record.HasPendingChanges, Is.False);
        }

        [Test]
        public void Json_field_order_does_not_create_a_pending_change()
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueJson(
                200,
                "{\"id\":\"rec_1\",\"created_at\":\"2026-08-16T00:00:00Z\",\"updated_at\":\"2026-08-17T00:00:00Z\",\"display_name\":\"Sword\",\"code_wire\":\"sword\"}",
                "\"a1\"");
            var set = CreateSet<InventoryItem>(fake, "ent_inventory");

            var record = set.LoadAsync("rec_1").GetAwaiter().GetResult();

            Assert.That(record.HasPendingChanges, Is.False);
        }

        [Test]
        public void Delete_marks_the_handle_deleted_only_after_server_success()
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueJson(200, RecordJson("rec_1", "sword", "Sword"), "\"a1\"");
            fake.EnqueueJson(204, string.Empty);
            var set = CreateSet<InventoryItem>(fake, "ent_inventory");
            var record = set.LoadAsync("rec_1").GetAwaiter().GetResult();

            record.DeleteAsync().GetAwaiter().GetResult();

            Assert.That(record.IsDeleted, Is.True);
            Assert.That(fake.Requests[1].Method, Is.EqualTo("DELETE"));
            Assert.That(fake.Requests[1].IfMatch, Is.EqualTo("\"a1\""));
            CaptureExceptionAsync<InvalidOperationException>(() => record.ReloadAsync()).GetAwaiter().GetResult();
        }

        [Test]
        public void Load_all_walks_cursors_and_preserves_page_order()
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueJson(
                200,
                QueryPageWithCursorJson(
                    "cursor-2",
                    true,
                    RecordJson("rec_1", "sword", "Sword"),
                    RecordJson("rec_2", "axe", "Axe")));
            fake.EnqueueJson(
                200,
                QueryPageWithCursorJson(
                    null,
                    false,
                    RecordJson("rec_3", "bow", "Bow")));
            var set = CreateSet<InventoryItem>(fake, "ent_inventory");

            var result = set.LoadAllAsync(pageSize: 2).GetAwaiter().GetResult();

            Assert.That(result.IsTruncated, Is.False);
            Assert.That(result.NextCursor, Is.Empty);
            Assert.That(
                result.Records.Select(record => record.Id),
                Is.EqualTo(new[] { "rec_1", "rec_2", "rec_3" }));
            Assert.That(fake.Requests[0].JsonBody, Does.Contain("\"limit\":2"));
            Assert.That(fake.Requests[1].JsonBody, Does.Contain("\"cursor\":\"cursor-2\""));
        }

        [Test]
        public void Load_many_preserves_input_order_and_returns_typed_item_failures()
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueJson(200, QueryPageJson(RecordJson("rec_3", "bow", "Bow"), RecordJson("rec_1", "sword", "Sword")));
            var set = CreateSet<InventoryItem>(fake, "ent_inventory");

            var result = set.LoadManyAsync(
                    new[] { "rec_1", "rec_missing", "rec_3" },
                    maxConcurrency: 1)
                .GetAwaiter().GetResult();

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.SucceededCount, Is.EqualTo(2));
            Assert.That(result.FailedCount, Is.EqualTo(1));
            Assert.That(result.Items.Select(item => item.Key), Is.EqualTo(
                new[] { "rec_1", "rec_missing", "rec_3" }));
            Assert.That(result.Items[1].Exception, Is.TypeOf<PlayServRecordNotFoundException>());
            Assert.That(result.Items[1].Error.Code, Is.EqualTo(PlayServErrorCode.NotFound));
        }

        [Test]
        public void Delete_by_id_sends_optional_etag_without_loading_a_handle()
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueJson(204, string.Empty);
            var set = CreateSet<InventoryItem>(fake, "ent_inventory");

            set.DeleteByIdAsync("rec_1", "\"known\"").GetAwaiter().GetResult();

            Assert.That(fake.Requests.Count, Is.EqualTo(1));
            Assert.That(fake.Requests[0].Method, Is.EqualTo("DELETE"));
            Assert.That(fake.Requests[0].RelativePath, Does.EndWith("/records/rec_1"));
            Assert.That(fake.Requests[0].IfMatch, Is.EqualTo("\"known\""));
        }

        [Test]
        public void Bulk_save_keeps_successful_rotation_and_reports_stale_item()
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueJson(200, RecordJson("rec_1", "sword", "Sword"), "\"a1\"");
            fake.EnqueueJson(200, RecordJson("rec_2", "axe", "Axe"), "\"b1\"");
            fake.EnqueueJson(200, RecordJson("rec_1", "sword", "Blade"), "\"a2\"");
            fake.EnqueueException(HttpError(
                412,
                "precondition_failed",
                "{\"code\":\"precondition_failed\"}"));
            var set = CreateSet<InventoryItem>(fake, "ent_inventory");
            var first = set.LoadAsync("rec_1").GetAwaiter().GetResult();
            var second = set.LoadAsync("rec_2").GetAwaiter().GetResult();
            first.Value.DisplayName = "Blade";
            second.Value.DisplayName = "Hatchet";

            var result = set.BulkSaveAsync(
                    new[] { first, second },
                    maxConcurrency: 1)
                .GetAwaiter().GetResult();

            Assert.That(result.SucceededCount, Is.EqualTo(1));
            Assert.That(result.FailedCount, Is.EqualTo(1));
            Assert.That(first.ETag, Is.EqualTo("\"a2\""));
            Assert.That(second.ETag, Is.EqualTo("\"b1\""));
            Assert.That(result.Items[1].Exception, Is.TypeOf<PlayServRecordConflictException>());
        }

        [Test]
        public void Delete_all_requires_exact_intent_and_never_partially_deletes_at_limit()
        {
            var noIoFake = new FakeDataHttpClient();
            var noIoSet = CreateSet<InventoryItem>(noIoFake, "ent_inventory");
            Assert.Throws<InvalidOperationException>(() =>
                noIoSet.DeleteAllAsync(
                        null,
                        PlayServDeleteAllConfirmation.MatchingRecords)
                    .GetAwaiter().GetResult());
            Assert.That(noIoFake.Requests, Is.Empty);

            var limitedFake = new FakeDataHttpClient();
            limitedFake.EnqueueJson(
                200,
                QueryPageWithCursorJson(
                    "cursor-2",
                    true,
                    RecordJson("rec_1", "sword", "Sword"),
                    RecordJson("rec_2", "axe", "Axe")));
            var limitedSet = CreateSet<InventoryItem>(limitedFake, "ent_inventory");

            var exception = CaptureExceptionAsync<PlayServDataException>(() =>
                    limitedSet.DeleteAllAsync(
                        null,
                        PlayServDeleteAllConfirmation.AllRecords,
                        maxRecords: 2,
                        maxConcurrency: 1))
                .GetAwaiter().GetResult();

            Assert.That(exception.BackendCode, Is.EqualTo("bulk_limit_exceeded"));
            Assert.That(limitedFake.Requests.Count, Is.EqualTo(1));
            Assert.That(limitedFake.Requests.All(request => request.Method != "DELETE"), Is.True);
        }

        [Test]
        public void Populate_many_uses_target_record_set_and_bounded_load_many_pipeline()
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueJson(200, QueryPageJson(RecordJson("rec_owner_1", "ada", "Ada"), RecordJson("rec_owner_2", "lin", "Lin")));
            var targetSet = CreateSet<InventoryItem>(fake, "ent_inventory");

            var result = targetSet.PopulateManyAsync(
                    new[] { "rec_owner_1", "rec_owner_2" },
                    maxConcurrency: 1)
                .GetAwaiter().GetResult();

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Items.Select(item => item.Value.Id), Is.EqualTo(
                new[] { "rec_owner_1", "rec_owner_2" }));
        }

        [Test]
        public void Partial_record_requires_full_reload_before_save()
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueJson(200, RecordJson("rec_1", "sword", null), "\"a1\"");
            fake.EnqueueJson(200, RecordJson("rec_1", "sword", "Sword"), "\"a2\"");
            fake.EnqueueJson(200, RecordJson("rec_1", "sword", "Renamed"), "\"a3\"");
            var set = CreateSet<InventoryItem>(fake, "ent_inventory");
            var record = set.LoadAsync(
                "rec_1",
                new PlayServLoadOptions
                {
                    Fields = new[] { "code_wire" },
                    Expand = new[] { "owner_relation" }
                }).GetAwaiter().GetResult();

            record.Value.Code = "changed";
            CaptureExceptionAsync<InvalidOperationException>(() => record.SaveAsync()).GetAwaiter().GetResult();
            record.ReloadAsync().GetAwaiter().GetResult();
            record.Value.DisplayName = "Renamed";
            record.SaveAsync().GetAwaiter().GetResult();

            Assert.That(record.IsPartial, Is.False);
            Assert.That(fake.Requests[0].RelativePath, Does.Contain("fields=code_wire"));
            Assert.That(fake.Requests[0].RelativePath, Does.Contain("expand=owner_relation"));
            Assert.That(fake.Requests[1].Method, Is.EqualTo("GET"));
            Assert.That(fake.Requests[2].Method, Is.EqualTo("PATCH"));
        }

        [Test]
        public void Query_uses_selector_wire_names_and_reads_bidirectional_cursor_page()
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueJson(200,
                "{\"data\":[" + RecordJson("rec_1", "sword", "Sword") +
                "],\"page\":{\"cursor_next\":\"next\",\"cursor_prev\":\"prev\",\"has_more\":true},\"total_estimate\":12}");
            var set = CreateSet<InventoryItem>(fake, "ent_inventory");
            var query = new PlayServRecordQuery<InventoryItem>()
                .Where(item => item.Code, PlayServQueryOperator.Eq, "sword")
                .OrderByDescending(item => item.DisplayName)
                .Search("blade")
                .Hide(item => item.DisplayName)
                .WithCursor("cursor-in")
                .WithLimit(10);

            var page = set.QueryAsync(query).GetAwaiter().GetResult();

            Assert.That(page.Records.Count, Is.EqualTo(1));
            Assert.That(page.Records[0].IsPartial, Is.True);
            Assert.That(page.NextCursor, Is.EqualTo("next"));
            Assert.That(page.PreviousCursor, Is.EqualTo("prev"));
            Assert.That(page.HasMore, Is.True);
            Assert.That(page.TotalEstimate, Is.EqualTo(12));
            Assert.That(fake.Requests[0].JsonBody, Does.Contain("\"field\":\"code_wire\""));
            Assert.That(fake.Requests[0].JsonBody, Does.Contain("\"field\":\"display_name\""));
            Assert.That(fake.Requests[0].JsonBody, Does.Contain("\"hidden_columns\":[\"display_name\"]"));
            Assert.That(fake.Requests[0].JsonBody, Does.Contain("\"cursor\":\"cursor-in\""));
        }

        [Test]
        public void Expression_query_flattens_and_maps_comparisons_in_and_null_checks()
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueJson(200, QueryPageJson());
            var set = CreateSet<QueryableInventoryItem>(fake, "ent_inventory");
            var minimum = 10;
            var codes = new[] { "sword", "axe" };
            var query = new PlayServRecordQuery<QueryableInventoryItem>()
                .Where(item => item.DisplayName != null && item.Durability >= minimum)
                .And(item => 5 < item.Durability)
                .And(item => codes.Contains(item.Code));

            set.QueryAsync(query).GetAwaiter().GetResult();

            var body = fake.Requests[0].JsonBody;
            Assert.That(body, Does.Contain("\"field\":\"display_name\",\"op\":\"is_not_null\""));
            Assert.That(body, Does.Contain("\"field\":\"durability\",\"op\":\"gte\",\"value\":10"));
            Assert.That(body, Does.Contain("\"field\":\"durability\",\"op\":\"gt\",\"value\":5"));
            Assert.That(body, Does.Contain("\"field\":\"code_wire\",\"op\":\"in\""));
            Assert.That(body, Does.Contain("\"sword\""));
            Assert.That(body, Does.Contain("\"axe\""));
        }

        [Test]
        public void Expression_query_rejects_field_to_field_and_or_expressions()
        {
            Assert.Throws<ArgumentException>(() =>
                new PlayServRecordQuery<QueryableInventoryItem>()
                    .Where(item => item.Code == item.DisplayName));
            Assert.Throws<ArgumentException>(() =>
                new PlayServRecordQuery<QueryableInventoryItem>()
                    .Where(item => item.Code == "sword" || item.Code == "axe"));
        }

        [Test]
        public void Select_fields_and_include_hydrate_query_records_with_etags_in_order()
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueJson(200, QueryPageJson(
                RecordJson("rec_1", "sword", "Sword"),
                RecordJson("rec_2", "axe", "Axe")));
            fake.EnqueueJson(200, RecordWithRelationJson("rec_1", "sword", "Sword", "Owner one"), "\"e1\"");
            fake.EnqueueJson(200, RecordWithRelationJson("rec_2", "axe", "Axe", "Owner two"), "\"e2\"");
            var set = CreateSet<QueryableInventoryItem>(fake, "ent_inventory");
            var query = new PlayServRecordQuery<QueryableInventoryItem>()
                .SelectFields(item => item.Code, item => item.DisplayName)
                .Include(item => item.OwnerRelation)
                .WithLimit(2);

            var page = set.QueryAsync(query).GetAwaiter().GetResult();

            Assert.That(fake.Requests.Count, Is.EqualTo(3));
            Assert.That(fake.Requests[1].Method, Is.EqualTo("GET"));
            Assert.That(fake.Requests[1].RelativePath, Does.Contain("/records/rec_1"));
            Assert.That(fake.Requests[1].RelativePath, Does.Contain("fields=code_wire"));
            Assert.That(fake.Requests[1].RelativePath, Does.Contain("expand=owner_relation"));
            Assert.That(fake.Requests[2].RelativePath, Does.Contain("/records/rec_2"));
            Assert.That(page.Records.Select(record => record.Id), Is.EqualTo(new[] { "rec_1", "rec_2" }));
            Assert.That(page.Records.Select(record => record.ETag), Is.EqualTo(new[] { "\"e1\"", "\"e2\"" }));
            Assert.That(page.Records.All(record => record.IsPartial), Is.True);
            Assert.That(page.Records[0].Value.OwnerRelation.Name, Is.EqualTo("Owner one"));
        }

        [Test]
        public void Query_hydration_failure_does_not_return_a_partial_page()
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueJson(200, QueryPageJson(
                RecordJson("rec_1", "sword", "Sword"),
                RecordJson("rec_2", "axe", "Axe")));
            fake.EnqueueException(HttpError(404, "record_not_found", "{}"));
            fake.EnqueueJson(200, RecordWithRelationJson("rec_2", "axe", "Axe", "Owner two"), "\"e2\"");
            var set = CreateSet<QueryableInventoryItem>(fake, "ent_inventory");
            var query = new PlayServRecordQuery<QueryableInventoryItem>()
                .SelectFields(item => item.Code)
                .Include(item => item.OwnerRelation);

            CaptureExceptionAsync<PlayServRecordNotFoundException>(() =>
                set.QueryAsync(query)).GetAwaiter().GetResult();

            Assert.That(fake.Requests.Count, Is.EqualTo(3));
        }

        [Test]
        public void Query_cancellation_happens_before_any_rest_request()
        {
            var fake = new FakeDataHttpClient();
            var set = CreateSet<QueryableInventoryItem>(fake, "ent_inventory");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            CaptureExceptionAsync<OperationCanceledException>(() =>
                set.QueryAsync(
                    new PlayServRecordQuery<QueryableInventoryItem>()
                        .SelectFields(item => item.Code),
                    ct: cancellation.Token)).GetAwaiter().GetResult();

            Assert.That(fake.Requests, Is.Empty);
        }

        [Test]
        public void Select_fields_and_hide_are_mutually_exclusive()
        {
            Assert.Throws<InvalidOperationException>(() =>
                new PlayServRecordQuery<QueryableInventoryItem>()
                    .SelectFields(item => item.Code)
                    .Hide(item => item.DisplayName));
            Assert.Throws<InvalidOperationException>(() =>
                new PlayServRecordQuery<QueryableInventoryItem>()
                    .Hide(item => item.DisplayName)
                    .SelectFields(item => item.Code));
        }

        [Test]
        public void Realtime_encoder_uses_same_wire_names_variables_projection_and_include()
        {
            var query = new PlayServRecordQuery<QueryableInventoryItem>()
                .Where(item => item.Code, PlayServQueryOperator.Equal, "sword")
                .And(item => item.Durability, PlayServQueryOperator.GreaterThanOrEqual, 10)
                .SelectFields(item => item.Code, item => item.Durability)
                .Include(item => item.OwnerRelation)
                .WithLimit(25);

            var payload = QueryBuilder.BuildCollectionQuery<QueryableInventoryItem>(
                "RuntimeInventory",
                query.Snapshot(),
                new NewtonsoftJsonCodec());

            Assert.That(payload.Query, Is.EqualTo(
                "RuntimeInventory(where: { code_wire: { eq: $filter0 }, durability: { gte: $filter1 } }, limit: $limit) " +
                "{ code_wire durability owner_relation { relation_name } }"));
            Assert.That(payload.Variables["filter0"], Is.EqualTo("sword"));
            Assert.That(payload.Variables["filter1"], Is.EqualTo(10));
            Assert.That(payload.Variables["limit"], Is.EqualTo(25));
            Assert.That(payload.Query, Does.Not.Contain("sword"));
        }

        [Test]
        public void Realtime_encoder_combines_common_filters_with_or_and_uses_variables()
        {
            var query = new PlayServRecordQuery<QueryableInventoryItem>()
                .Where(item => item.Durability >= 10)
                .Or(
                    item => item.Code == "sword",
                    item => item.Code == "axe" && item.Durability < 50)
                .SelectFields(item => item.Code)
                .WithLimit(20);

            var payload = QueryBuilder.BuildCollectionQuery<QueryableInventoryItem>(
                "RuntimeInventory",
                query.Snapshot(),
                new NewtonsoftJsonCodec());

            Assert.That(payload.Query, Is.EqualTo(
                "RuntimeInventory(where: { durability: { gte: $filter0 }, or: [ " +
                "{ code_wire: { eq: $or0Filter0 } }, " +
                "{ code_wire: { eq: $or1Filter0 }, durability: { lt: $or1Filter1 } } ] }, " +
                "limit: $limit) { code_wire }"));
            Assert.That(payload.Variables["filter0"], Is.EqualTo(10));
            Assert.That(payload.Variables["or0Filter0"], Is.EqualTo("sword"));
            Assert.That(payload.Variables["or1Filter0"], Is.EqualTo("axe"));
            Assert.That(payload.Variables["or1Filter1"], Is.EqualTo(50));
            Assert.That(payload.Query, Does.Not.Contain("sword"));
            Assert.That(payload.Query, Does.Not.Contain("axe"));
        }

        [Test]
        public void Realtime_nested_include_merges_paths_and_maps_every_wire_name()
        {
            var query = new PlayServRecordQuery<NestedInventoryItem>()
                .SelectFields(item => item.Code)
                .Include(item => item.Guild)
                .Include(item => item.Guild.Owner)
                .Include("guild_wire.owner_wire")
                .WithLimit(5);

            var payload = QueryBuilder.BuildCollectionQuery<NestedInventoryItem>(
                "NestedInventory",
                query.Snapshot(),
                new NewtonsoftJsonCodec());

            Assert.That(payload.Query, Is.EqualTo(
                "NestedInventory(limit: $limit) { code_wire guild_wire { guild_name owner_wire { relation_name } } }"));
        }

        [Test]
        public void Or_and_nested_include_are_rejected_by_rest_before_catalogue_io()
        {
            var fake = new FakeDataHttpClient();
            var set = CreateSet<NestedInventoryItem>(fake, "ent_nested");

            var orError = CaptureExceptionAsync<PlayServQueryCapabilityException>(() =>
                set.QueryAsync(new PlayServRecordQuery<NestedInventoryItem>()
                    .Or(item => item.Code == "sword", item => item.Code == "axe")))
                .GetAwaiter().GetResult();
            Assert.That(orError.Target, Is.EqualTo(PlayServQueryTarget.Rest));
            Assert.That(orError.UnsupportedFeatures, Does.Contain("or"));

            var includeError = CaptureExceptionAsync<PlayServQueryCapabilityException>(() =>
                set.QueryAsync(new PlayServRecordQuery<NestedInventoryItem>()
                    .Include(item => item.Guild.Owner)))
                .GetAwaiter().GetResult();
            Assert.That(includeError.Target, Is.EqualTo(PlayServQueryTarget.Rest));
            Assert.That(includeError.UnsupportedFeatures, Does.Contain("nested include"));
            Assert.That(fake.AllRequests, Is.Empty);
        }

        [Test]
        public void Realtime_or_validates_group_and_operator_limits_before_io()
        {
            var tooMany = Enumerable.Range(0, 9)
                .Select(index => (Expression<Func<QueryableInventoryItem, bool>>)
                    (item => item.Durability == index))
                .ToArray();
            Assert.Throws<ArgumentException>(() =>
                new PlayServRecordQuery<QueryableInventoryItem>().Or(tooMany));

            var fake = new FakeDataHttpClient();
            var opened = false;
            var set = CreateSet<QueryableInventoryItem>(
                fake,
                "ent_inventory",
                subscribeAsync: (query, variables, ct) =>
                {
                    opened = true;
                    return Task.FromResult<ISharedCollection<QueryableInventoryItem>>(
                        new FakeSharedCollection<QueryableInventoryItem>());
                });
            var values = new[] { "sword", "axe" };
            var capability = CaptureExceptionAsync<PlayServQueryCapabilityException>(() =>
                set.SubscribeAsync(new PlayServRecordQuery<QueryableInventoryItem>()
                    .Or(item => values.Contains(item.Code))))
                .GetAwaiter().GetResult();

            Assert.That(capability.Target, Is.EqualTo(PlayServQueryTarget.Realtime));
            Assert.That(capability.UnsupportedFeatures, Does.Contain("in"));
            Assert.That(opened, Is.False);
            Assert.That(fake.AllRequests, Is.Empty);
        }

        [Test]
        public void Nested_include_validates_depth_and_ignored_segments()
        {
            Assert.Throws<ArgumentException>(() =>
                new PlayServRecordQuery<NestedInventoryItem>()
                    .Include("one.two.three.four.five.six.seven"));
            Assert.Throws<ArgumentException>(() =>
                new PlayServRecordQuery<NestedInventoryItem>()
                    .Include(item => item.Guild.IgnoredOwner));
        }

        [Test]
        public void Realtime_rejects_non_native_features_before_subscription_io()
        {
            var fake = new FakeDataHttpClient();
            var opened = false;
            var set = CreateSet<QueryableInventoryItem>(
                fake,
                "ent_inventory",
                subscribeAsync: (query, variables, ct) =>
                {
                    opened = true;
                    return Task.FromResult<ISharedCollection<QueryableInventoryItem>>(
                        new FakeSharedCollection<QueryableInventoryItem>());
                });
            var codes = new[] { "sword", "axe" };
            var query = new PlayServRecordQuery<QueryableInventoryItem>()
                .Where(item => codes.Contains(item.Code) && item.DisplayName != null)
                .OrderBy(item => item.Code)
                .Search("blade")
                .Hide(item => item.Durability)
                .WithCursor("next");

            var exception = CaptureExceptionAsync<PlayServQueryCapabilityException>(() =>
                set.SubscribeAsync(query)).GetAwaiter().GetResult();

            Assert.That(exception.Target, Is.EqualTo(PlayServQueryTarget.Realtime));
            Assert.That(exception.UnsupportedFeatures, Does.Contain("in"));
            Assert.That(exception.UnsupportedFeatures, Does.Contain("is_not_null"));
            Assert.That(exception.UnsupportedFeatures, Does.Contain("sort"));
            Assert.That(exception.UnsupportedFeatures, Does.Contain("search"));
            Assert.That(exception.UnsupportedFeatures, Does.Contain("hidden field projection"));
            Assert.That(exception.UnsupportedFeatures, Does.Contain("cursor"));
            Assert.That(fake.Requests, Is.Empty);
            Assert.That(opened, Is.False);
        }

        [Test]
        public void Explicit_entity_subscription_resolves_canonical_schema_name()
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueCatalogueJson(
                "{\"data\":[{\"entity_id\":\"ent_inventory\",\"name\":\"RuntimeInventory\",\"singleton\":false}]}");
            string sentQuery = null;
            Dictionary<string, object> sentVariables = null;
            var set = CreateSet<QueryableInventoryItem>(
                fake,
                "ent_inventory",
                subscribeAsync: (query, variables, ct) =>
                {
                    sentQuery = query;
                    sentVariables = variables;
                    return Task.FromResult<ISharedCollection<QueryableInventoryItem>>(
                        new FakeSharedCollection<QueryableInventoryItem>());
                });

            set.SubscribeAsync(
                new PlayServRecordQuery<QueryableInventoryItem>()
                    .SelectFields(item => item.Code)
                    .WithLimit(5)).GetAwaiter().GetResult();

            Assert.That(fake.AllRequests.Count, Is.EqualTo(1));
            Assert.That(fake.AllRequests[0].RelativePath, Is.EqualTo("data/tables"));
            Assert.That(sentQuery, Is.EqualTo("RuntimeInventory(limit: $limit) { code_wire }"));
            Assert.That(sentVariables["limit"], Is.EqualTo(5));
        }

        [Test]
        public void Legacy_keyed_where_fails_instead_of_being_ignored()
        {
            using var adapter = new PlayServDataSubscriptionAdapter(
                new FakeCommandBus(),
                new FakeLogger(),
                new NewtonsoftJsonCodec());
#pragma warning disable 618
            var builder = new SharedEntityBuilder<QueryableInventoryItem>(adapter, "RuntimeInventory")
                .Key("rec_1")
                .Where(item => item.Code == "sword");
#pragma warning restore 618

            var exception = CaptureExceptionAsync<PlayServQueryCapabilityException>(() =>
                builder.BindAsync()).GetAwaiter().GetResult();

            Assert.That(exception.Target, Is.EqualTo(PlayServQueryTarget.KeyedEntitySubscription));
            Assert.That(exception.UnsupportedFeatures, Does.Contain("Where"));
        }

        [Test]
        public void Legacy_keyed_projection_and_include_use_wire_names()
        {
            Expression<Func<QueryableInventoryItem, InventoryProjection>> projection = item =>
                new InventoryProjection { Code = item.Code };
            Expression<Func<QueryableInventoryItem, OwnerRelation>> include = item => item.OwnerRelation;

            var query = QueryBuilder.BuildKeyedQuery(
                "RuntimeInventory",
                "rec_1",
                typeof(QueryableInventoryItem),
                projection,
                new LambdaExpression[] { include },
                new NewtonsoftJsonCodec());

            Assert.That(query, Is.EqualTo(
                "RuntimeInventory(id: $id) { code_wire owner_relation { relation_name } }"));
        }

        [Test]
        public void Load_or_create_does_not_call_factory_for_existing_record()
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueJson(200, RecordJson("rec_existing", "sword", "Sword"), "\"a1\"");
            var set = CreateSet<InventoryItem>(fake, "ent_inventory");
            var calls = 0;

            var result = set.LoadOrCreateAsync(
                PlayServNaturalKey<InventoryItem>.For(item => item.Code, "sword"),
                () =>
                {
                    calls++;
                    return new InventoryItem { Code = "sword" };
                }).GetAwaiter().GetResult();

            Assert.That(result.WasCreated, Is.False);
            Assert.That(result.Record.Id, Is.EqualTo("rec_existing"));
            Assert.That(calls, Is.Zero);
            Assert.That(fake.Requests[0].Method, Is.EqualTo("GET"));
            Assert.That(fake.Requests[0].RelativePath,
                Is.EqualTo("data/tables/ent_inventory/records:by-natural-key?field=code_wire&value=sword"));
        }

        [Test]
        public void Load_or_create_verifies_created_server_id()
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueException(HttpError(404, "not_found", "{}"));
            fake.EnqueueJson(201, RecordJson("rec_created", "sword", "Mine"), "\"a1\"");
            fake.EnqueueJson(200, RecordJson("rec_created", "sword", "Mine"), "\"a2\"");
            var set = CreateSet<InventoryItem>(fake, "ent_inventory");

            var result = set.LoadOrCreateAsync(
                PlayServNaturalKey<InventoryItem>.For(item => item.Code, "sword"),
                () => new InventoryItem { Code = "sword", DisplayName = "Mine" }).GetAwaiter().GetResult();

            Assert.That(result.WasCreated, Is.True);
            Assert.That(result.Record.Id, Is.EqualTo("rec_created"));
            Assert.That(result.Record.ETag, Is.EqualTo("\"a2\""));
            Assert.That(fake.Requests.Count, Is.EqualTo(3));
            Assert.That(fake.Requests[1].Method, Is.EqualTo("POST"));
            Assert.That(fake.Requests[2].Method, Is.EqualTo("GET"));
            Assert.That(fake.Requests[2].RelativePath, Does.EndWith("/records/rec_created"));
        }

        [Test]
        public void Load_or_create_recovers_phantom_create_with_second_query()
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueException(HttpError(404, "not_found", "{}"));
            fake.EnqueueJson(201, RecordJson("rec_phantom", "sword", "Mine"), "\"a1\"");
            fake.EnqueueException(HttpError(404, "record_not_found", "{}"));
            fake.EnqueueJson(200, RecordJson("rec_racer", "sword", "Racer"), "\"a2\"");
            var set = CreateSet<InventoryItem>(fake, "ent_inventory");

            var result = set.LoadOrCreateAsync(
                PlayServNaturalKey<InventoryItem>.For(item => item.Code, "sword"),
                () => new InventoryItem { Code = "sword", DisplayName = "Mine" }).GetAwaiter().GetResult();

            Assert.That(result.WasCreated, Is.False);
            Assert.That(result.Record.Id, Is.EqualTo("rec_racer"));
            Assert.That(fake.Requests.Count, Is.EqualTo(4));
            Assert.That(fake.Requests[3].RelativePath, Does.Contain("/records:by-natural-key?"));
        }

        [Test]
        public void Load_or_create_reports_created_record_not_persisted_when_recovery_is_empty()
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueException(HttpError(404, "not_found", "{}"));
            fake.EnqueueJson(201, RecordJson("rec_phantom", "sword", "Mine"), "\"a1\"");
            fake.EnqueueException(HttpError(404, "record_not_found", "{}"));
            fake.EnqueueException(HttpError(404, "not_found", "{}"));
            var set = CreateSet<InventoryItem>(fake, "ent_inventory");

            var exception = CaptureExceptionAsync<PlayServDataException>(() =>
                set.LoadOrCreateAsync(
                    PlayServNaturalKey<InventoryItem>.For(item => item.Code, "sword"),
                    () => new InventoryItem { Code = "sword", DisplayName = "Mine" }))
                .GetAwaiter().GetResult();

            Assert.That(exception.BackendCode, Is.EqualTo("created_record_not_persisted"));
            Assert.That(exception.UnifiedError.Code, Is.EqualTo(PlayServErrorCode.InvalidResponse));
            Assert.That(exception.Extensions["record_id"], Is.EqualTo("rec_phantom"));
        }

        [Test]
        public void Load_or_create_surfaces_non_unique_natural_key_conflict()
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueException(HttpError(
                409,
                "natural_key_not_unique",
                "{\"code\":\"natural_key_not_unique\",\"detail\":\"duplicate natural key\"}"));
            var set = CreateSet<InventoryItem>(fake, "ent_inventory");
            var calls = 0;

            var exception = CaptureExceptionAsync<PlayServRecordConflictException>(() =>
                set.LoadOrCreateAsync(
                    PlayServNaturalKey<InventoryItem>.For(item => item.Code, "sword"),
                    () =>
                    {
                        calls++;
                        return new InventoryItem { Code = "sword" };
                    })).GetAwaiter().GetResult();

            Assert.That(exception.BackendCode, Is.EqualTo("natural_key_not_unique"));
            Assert.That(calls, Is.Zero);
            Assert.That(fake.Requests.Count, Is.EqualTo(1));
        }

        [Test]
        public void Load_or_create_rejects_factory_natural_key_mismatch_before_create()
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueException(HttpError(404, "not_found", "{}"));
            var set = CreateSet<InventoryItem>(fake, "ent_inventory");

            CaptureExceptionAsync<InvalidOperationException>(() =>
                set.LoadOrCreateAsync(
                    PlayServNaturalKey<InventoryItem>.For(item => item.Code, "sword"),
                    () => new InventoryItem { Code = "shield" })).GetAwaiter().GetResult();

            Assert.That(fake.Requests.Count, Is.EqualTo(1));
        }

        [Test]
        public void Load_or_create_does_not_mask_unrelated_conflict()
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueException(HttpError(404, "not_found", "{}"));
            fake.EnqueueException(HttpError(409, "conflict", "{\"code\":\"conflict\"}"));
            var set = CreateSet<InventoryItem>(fake, "ent_inventory");

            var exception = CaptureExceptionAsync<PlayServRecordConflictException>(() =>
                set.LoadOrCreateAsync(
                    PlayServNaturalKey<InventoryItem>.For(item => item.Code, "sword"),
                    () => new InventoryItem { Code = "sword" })).GetAwaiter().GetResult();

            Assert.That(exception.Kind, Is.EqualTo(PlayServRecordConflictKind.Unknown));
            Assert.That(exception.UnifiedError.Code, Is.EqualTo(PlayServErrorCode.Conflict));
            Assert.That(fake.Requests.Count, Is.EqualTo(2));
        }

        [Test]
        public void Load_or_create_cancellation_does_not_call_factory_or_recovery()
        {
            var fake = new FakeDataHttpClient();
            var set = CreateSet<InventoryItem>(fake, "ent_inventory");
            var calls = 0;
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            CaptureExceptionAsync<OperationCanceledException>(() =>
                set.LoadOrCreateAsync(
                    PlayServNaturalKey<InventoryItem>.For(item => item.Code, "sword"),
                    () =>
                    {
                        calls++;
                        return new InventoryItem { Code = "sword" };
                    },
                    cancellation.Token)).GetAwaiter().GetResult();

            Assert.That(calls, Is.Zero);
            Assert.That(fake.Requests, Is.Empty);
        }

        [Test]
        public void Structured_stale_etag_maps_to_record_conflict()
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueJson(200, RecordJson("rec_1", "sword", "Sword"), "\"a1\"");
            fake.EnqueueException(HttpError(
                412,
                "precondition_failed",
                "{\"code\":\"precondition_failed\",\"detail\":\"stale\"}"));
            var set = CreateSet<InventoryItem>(fake, "ent_inventory");

            var exception = CaptureExceptionAsync<PlayServRecordConflictException>(async () =>
            {
                var record = await set.LoadAsync("rec_1");
                record.Value.Code = "shield";
                await record.SaveAsync();
            }).GetAwaiter().GetResult();

            Assert.That(exception.Kind, Is.EqualTo(PlayServRecordConflictKind.StaleVersion));
            Assert.That(exception.StatusCode, Is.EqualTo(412));
        }

        [Test]
        public void Current_custom_player_jwt_is_resolved_for_every_request()
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueJson(200, RecordJson("rec_1", "sword", "Sword"), "\"a1\"");
            var settings = Settings();
            settings.RuntimeTokenProvider = new PlayServDelegateRuntimeTokenProvider(
                _ => Task.FromResult("jwt-current"));
            var set = CreateSet<InventoryItem>(fake, "ent_inventory", settings);

            set.LoadAsync("rec_1").GetAwaiter().GetResult();

            Assert.That(fake.Requests[0].BearerToken, Is.EqualTo("jwt-current"));
            Assert.That(fake.Requests[0].ClientToken, Is.EqualTo("pk_test"));
        }

        [Test]
        public void Singleton_supports_load_diff_save_and_reload_metadata()
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueJson(200, SingletonJson("Welcome"), "\"b1\"");
            fake.EnqueueJson(200, SingletonJson("Hello"), "\"b2\"");
            var set = CreateSet<GameConfig>(fake, "ent_config");
            var singleton = set.GetSingletonAsync().GetAwaiter().GetResult();

            singleton.Value.Message = "Hello";
            singleton.SaveAsync().GetAwaiter().GetResult();

            Assert.That(fake.Requests[1].RelativePath, Is.EqualTo("data/tables/ent_config/singleton"));
            Assert.That(fake.Requests[1].IfMatch, Is.EqualTo("\"b1\""));
            Assert.That(singleton.ETag, Is.EqualTo("\"b2\""));
            Assert.That(singleton.HasPendingChanges, Is.False);
        }

        [Test]
        public void Record_subscription_uses_keyed_select_all_and_refreshes_canonical_metadata()
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueJson(200, RecordJson("rec_1", "sword", "Sword"), "\"a1\"");
            var bus = new RecordSubscriptionCommandBus
            {
                InitialRecordData = RecordFields("sword", "Sword")
            };
            using var adapter = new PlayServDataSubscriptionAdapter(
                bus,
                new FakeLogger(),
                new NewtonsoftJsonCodec());
            var set = CreateSet<InventoryItem>(
                fake,
                "ent_inventory",
                subscribeRecordAsync: adapter.SelectTypedRecordAsync);
            var record = set.LoadAsync("rec_1").GetAwaiter().GetResult();
            using var subscription = record.SubscribeAsync().GetAwaiter().GetResult();
            PlayServRecordChange<InventoryItem> change = null;
            subscription.Changed += value => change = value;
            fake.EnqueueJson(
                200,
                RecordJson("rec_1", "axe", "Battle Axe", "2026-08-18T00:00:00Z"),
                "\"a2\"");

            bus.EmitRecordUpdate(RecordFields("axe", "Battle Axe"));

            Assert.That(bus.OpenCount, Is.EqualTo(1));
            Assert.That(bus.LastOpen.Query, Is.EqualTo("InventoryItem(id: $id)"));
            Assert.That(bus.LastOpen.Variables["id"], Is.EqualTo("rec_1"));
            Assert.That(record.Value.Code, Is.EqualTo("axe"));
            Assert.That(record.Value.DisplayName, Is.EqualTo("Battle Axe"));
            Assert.That(record.ETag, Is.EqualTo("\"a2\""));
            Assert.That(record.UpdatedAt, Is.EqualTo(DateTimeOffset.Parse("2026-08-18T00:00:00Z")));
            Assert.That(record.HasPendingChanges, Is.False);
            Assert.That(change, Is.Not.Null);
            Assert.That(change.ChangedFields, Is.EqualTo(new[] { "code_wire", "display_name" }));
            Assert.That(change.OverwrotePendingChanges, Is.False);
        }

        [Test]
        public void Record_subscription_public_refresh_waits_for_value_snapshot_and_etag_sync()
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueJson(200, RecordJson("rec_1", "sword", "Sword"), "\"a1\"");
            var bus = new RecordSubscriptionCommandBus
            {
                InitialRecordData = RecordFields("sword", "Sword"),
                RefreshRecordData = RecordFields("bow", "Long Bow")
            };
            using var adapter = new PlayServDataSubscriptionAdapter(
                bus,
                new FakeLogger(),
                new NewtonsoftJsonCodec());
            var set = CreateSet<InventoryItem>(
                fake,
                "ent_inventory",
                subscribeRecordAsync: adapter.SelectTypedRecordAsync);
            var record = set.LoadAsync("rec_1").GetAwaiter().GetResult();
            using var subscription = record.SubscribeAsync().GetAwaiter().GetResult();
            PlayServRecordChange<InventoryItem> change = null;
            subscription.Changed += value => change = value;
            fake.EnqueueJson(
                200,
                RecordJson("rec_1", "bow", "Long Bow", "2026-08-19T12:00:00Z"),
                "\"a4\"");

            subscription.RefreshAsync().GetAwaiter().GetResult();

            Assert.That(bus.RefreshCount, Is.EqualTo(1));
            Assert.That(bus.LastRefresh.SubscriptionId, Is.EqualTo(71));
            Assert.That(record.Value.Code, Is.EqualTo("bow"));
            Assert.That(record.Value.DisplayName, Is.EqualTo("Long Bow"));
            Assert.That(record.ETag, Is.EqualTo("\"a4\""));
            Assert.That(record.UpdatedAt, Is.EqualTo(DateTimeOffset.Parse("2026-08-19T12:00:00Z")));
            Assert.That(record.HasPendingChanges, Is.False);
            Assert.That(change, Is.Not.Null);
            Assert.That(change.ChangedFields, Is.EqualTo(new[] { "code_wire", "display_name" }));
        }

        [Test]
        public void Record_subscription_reports_pending_conflict_then_applies_backend_wins()
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueJson(200, RecordJson("rec_1", "sword", "Sword"), "\"a1\"");
            var bus = new RecordSubscriptionCommandBus
            {
                InitialRecordData = RecordFields("sword", "Sword")
            };
            using var adapter = new PlayServDataSubscriptionAdapter(
                bus,
                new FakeLogger(),
                new NewtonsoftJsonCodec());
            var set = CreateSet<InventoryItem>(
                fake,
                "ent_inventory",
                subscribeRecordAsync: adapter.SelectTypedRecordAsync);
            var record = set.LoadAsync("rec_1").GetAwaiter().GetResult();
            using var subscription = record.SubscribeAsync().GetAwaiter().GetResult();
            var notifications = new List<string>();
            PlayServRecordRealtimeConflict<InventoryItem> conflict = null;
            PlayServRecordChange<InventoryItem> change = null;
            subscription.Conflict += value =>
            {
                conflict = value;
                notifications.Add("conflict");
            };
            subscription.Changed += value =>
            {
                change = value;
                notifications.Add("changed");
            };
            record.Value.DisplayName = "Local Rename";
            fake.EnqueueJson(
                200,
                RecordJson("rec_1", "sword", "Remote Rename", "2026-08-18T00:00:00Z"),
                "\"a2\"");

            bus.EmitRecordUpdate(RecordFields("sword", "Remote Rename"));

            Assert.That(conflict, Is.Not.Null);
            Assert.That(conflict.LocalValue.DisplayName, Is.EqualTo("Local Rename"));
            Assert.That(conflict.RemoteValue.DisplayName, Is.EqualTo("Remote Rename"));
            Assert.That(conflict.LocalChangedFields, Is.EqualTo(new[] { "display_name" }));
            Assert.That(conflict.RemoteChangedFields, Is.EqualTo(new[] { "display_name" }));
            Assert.That(change.OverwrotePendingChanges, Is.True);
            Assert.That(notifications, Is.EqualTo(new[] { "conflict", "changed" }));
            Assert.That(record.Value.DisplayName, Is.EqualTo("Remote Rename"));
            Assert.That(record.HasPendingChanges, Is.False);
        }

        [Test]
        public void Record_subscription_marks_server_deleted_record_and_terminates_once()
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueJson(200, RecordJson("rec_1", "sword", "Sword"), "\"a1\"");
            var bus = new RecordSubscriptionCommandBus
            {
                InitialRecordData = RecordFields("sword", "Sword")
            };
            using var adapter = new PlayServDataSubscriptionAdapter(
                bus,
                new FakeLogger(),
                new NewtonsoftJsonCodec());
            var set = CreateSet<InventoryItem>(
                fake,
                "ent_inventory",
                subscribeRecordAsync: adapter.SelectTypedRecordAsync);
            var record = set.LoadAsync("rec_1").GetAwaiter().GetResult();
            using var subscription = record.SubscribeAsync().GetAwaiter().GetResult();
            var failures = 0;
            var errors = 0;
            var terminated = 0;
            subscription.Failure += _ => failures++;
            subscription.Error += _ => errors++;
            subscription.Terminated += () => terminated++;

            bus.EmitTermination(49001, "record deleted");
            bus.EmitTermination(49001, "duplicate terminal frame");

            Assert.That(record.IsDeleted, Is.True);
            Assert.That(record.Value.DisplayName, Is.EqualTo("Sword"));
            Assert.That(subscription.State, Is.EqualTo(PlayServSubscriptionState.Terminated));
            Assert.That(subscription.TerminalError.Code, Is.EqualTo(PlayServErrorCode.SubscriptionTerminated));
            Assert.That(failures, Is.EqualTo(1));
            Assert.That(errors, Is.EqualTo(1));
            Assert.That(terminated, Is.EqualTo(1));
        }

        [Test]
        public void Record_subscription_rejects_pending_changes_before_transport_io()
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueJson(200, RecordJson("rec_1", "sword", "Sword"), "\"a1\"");
            var bus = new RecordSubscriptionCommandBus
            {
                InitialRecordData = RecordFields("sword", "Sword")
            };
            using var adapter = new PlayServDataSubscriptionAdapter(
                bus,
                new FakeLogger(),
                new NewtonsoftJsonCodec());
            var set = CreateSet<InventoryItem>(
                fake,
                "ent_inventory",
                subscribeRecordAsync: adapter.SelectTypedRecordAsync);
            var record = set.LoadAsync("rec_1").GetAwaiter().GetResult();
            record.Value.DisplayName = "Unsaved";

            var exception = Assert.Throws<InvalidOperationException>(() =>
                record.SubscribeAsync().GetAwaiter().GetResult());

            Assert.That(exception.Message, Does.Contain("unsaved local changes"));
            Assert.That(bus.OpenCount, Is.Zero);
        }

        [Test]
        public void Partial_record_is_reloaded_before_record_subscription_opens()
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueJson(200, RecordJson("rec_1", "sword", null), "\"a1\"");
            fake.EnqueueJson(200, RecordJson("rec_1", "sword", "Sword"), "\"a2\"");
            var bus = new RecordSubscriptionCommandBus
            {
                InitialRecordData = RecordFields("sword", "Sword")
            };
            using var adapter = new PlayServDataSubscriptionAdapter(
                bus,
                new FakeLogger(),
                new NewtonsoftJsonCodec());
            var set = CreateSet<InventoryItem>(
                fake,
                "ent_inventory",
                subscribeRecordAsync: adapter.SelectTypedRecordAsync);
            var record = set.LoadAsync(
                "rec_1",
                new PlayServLoadOptions { Fields = new[] { "code_wire" } }).GetAwaiter().GetResult();

            using var subscription = record.SubscribeAsync().GetAwaiter().GetResult();

            Assert.That(record.IsPartial, Is.False);
            Assert.That(record.Value.DisplayName, Is.EqualTo("Sword"));
            Assert.That(record.ETag, Is.EqualTo("\"a2\""));
            Assert.That(fake.Requests, Has.Count.EqualTo(2));
            Assert.That(fake.Requests[0].RelativePath, Does.Contain("fields=code_wire"));
            Assert.That(fake.Requests[1].RelativePath, Does.Not.Contain("fields="));
            Assert.That(bus.OpenCount, Is.EqualTo(1));
        }

        [Test]
        public void Record_subscription_replay_after_reconnect_refreshes_same_handle()
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueJson(200, RecordJson("rec_1", "sword", "Sword"), "\"a1\"");
            var bus = new RecordSubscriptionCommandBus
            {
                InitialRecordData = RecordFields("sword", "Sword")
            };
            using var adapter = new PlayServDataSubscriptionAdapter(
                bus,
                new FakeLogger(),
                new NewtonsoftJsonCodec());
            var set = CreateSet<InventoryItem>(
                fake,
                "ent_inventory",
                subscribeRecordAsync: adapter.SelectTypedRecordAsync);
            var record = set.LoadAsync("rec_1").GetAwaiter().GetResult();
            using var subscription = record.SubscribeAsync().GetAwaiter().GetResult();
            var changes = 0;
            subscription.Changed += _ => changes++;
            fake.EnqueueJson(
                200,
                RecordJson("rec_1", "shield", "Shield", "2026-08-19T00:00:00Z"),
                "\"a3\"");
            bus.InitialRecordData = RecordFields("shield", "Shield");

            adapter.OnConnected();

            Assert.That(bus.OpenCount, Is.EqualTo(2));
            Assert.That(subscription.State, Is.EqualTo(PlayServSubscriptionState.Active));
            Assert.That(record.Value.Code, Is.EqualTo("shield"));
            Assert.That(record.ETag, Is.EqualTo("\"a3\""));
            Assert.That(changes, Is.EqualTo(1));
        }

        private static PlayServRecordSet<T> CreateSet<T>(
            FakeDataHttpClient fake,
            string explicitEntityId,
            PlayServSettings settings = null,
            Func<
                string,
                Dictionary<string, object>,
                CancellationToken,
                Task<ISharedCollection<T>>> subscribeAsync = null,
            PlayServRecordSubscribeDelegate<T> subscribeRecordAsync = null)
        {
            if (!string.IsNullOrWhiteSpace(explicitEntityId))
            {
                fake.ConfigureDefaultCatalogue(
                    explicitEntityId,
                    typeof(T).Name,
                    typeof(T) == typeof(GameConfig));
            }
            var json = new NewtonsoftJsonCodec();
            var client = new PlayServRecordsClient(settings ?? Settings(), fake, json);
            return new PlayServRecordSet<T>(
                client,
                explicitEntityId,
                subscribeAsync,
                subscribeRecordAsync);
        }

        private static PlayServSettings Settings() => new PlayServSettings
        {
            ClientToken = "pk_test",
            BackendServerAddress = "https://records-" + Guid.NewGuid().ToString("N") + ".test"
        };

        private static async Task<TException> CaptureExceptionAsync<TException>(Func<Task> action)
            where TException : Exception
        {
            try
            {
                await action();
            }
            catch (TException exception)
            {
                return exception;
            }

            Assert.Fail("Expected exception of type " + typeof(TException).Name + ".");
            return null;
        }

        private static string RecordJson(
            string id,
            string code,
            string displayName,
            string updatedAt = "2026-08-17T00:00:00Z") =>
            "{\"id\":\"" + id + "\",\"created_at\":\"2026-08-16T00:00:00Z\",\"updated_at\":\"" +
            updatedAt + "\",\"owner\":\"plr_1\",\"code_wire\":\"" + code + "\",\"display_name\":" +
            (displayName == null ? "null" : "\"" + displayName + "\"") + "}";

        private static string RecordWithRelationJson(
            string id,
            string code,
            string displayName,
            string relationName) =>
            "{\"id\":\"" + id +
            "\",\"created_at\":\"2026-08-16T00:00:00Z\",\"updated_at\":\"2026-08-17T00:00:00Z\"," +
            "\"owner\":\"plr_1\",\"code_wire\":\"" + code + "\",\"display_name\":\"" + displayName +
            "\",\"owner_relation\":{\"relation_name\":\"" + relationName + "\"}}";

        private static string SingletonJson(string message) =>
            "{\"created_at\":\"2026-08-16T00:00:00Z\",\"updated_at\":\"2026-08-17T00:00:00Z\",\"message\":\"" +
            message + "\"}";

        private static string QueryPageJson(params string[] records) =>
            "{\"data\":[" + string.Join(",", records ?? Array.Empty<string>()) +
            "],\"page\":{\"cursor_next\":null,\"cursor_prev\":null,\"has_more\":false},\"total_estimate\":" +
            (records?.Length ?? 0) + "}";

        private static string QueryPageWithCursorJson(
            string nextCursor,
            bool hasMore,
            params string[] records) =>
            "{\"data\":[" + string.Join(",", records ?? Array.Empty<string>()) +
            "],\"page\":{\"cursor_next\":" +
            (nextCursor == null ? "null" : "\"" + nextCursor + "\"") +
            ",\"cursor_prev\":null,\"has_more\":" + (hasMore ? "true" : "false") +
            "},\"total_estimate\":" + (records?.Length ?? 0) + "}";

        private static Dictionary<string, object> RecordFields(string code, string displayName) =>
            new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["code_wire"] = code,
                ["display_name"] = displayName
            };

        private static string AclCatalogueJson(
            string entityId,
            string name,
            bool singleton,
            string readPolicy,
            bool clientRead,
            bool clientWrite,
            bool serverRead = true,
            bool serverWrite = true,
            bool backendRead = true,
            bool backendWrite = true) =>
            "{\"data\":[{\"entity_id\":\"" + entityId +
            "\",\"name\":\"" + name +
            "\",\"singleton\":" + (singleton ? "true" : "false") +
            ",\"read\":\"" + readPolicy +
            "\",\"acl\":{\"client\":{\"read\":" + (clientRead ? "true" : "false") +
            ",\"write\":" + (clientWrite ? "true" : "false") +
            "},\"server\":{\"read\":" + (serverRead ? "true" : "false") +
            ",\"write\":" + (serverWrite ? "true" : "false") +
            "},\"backend\":{\"read\":" + (backendRead ? "true" : "false") +
            ",\"write\":" + (backendWrite ? "true" : "false") + "}}}]}";

        private static PlayServRuntimeHttpException HttpError(int status, string code, string body) =>
            new PlayServRuntimeHttpException(
                "HTTP " + status,
                status,
                body,
                code,
                false,
                problemDetail: code);

        private sealed class InventoryItem
        {
            public string Id { get; set; }

            [PlayServField("code_wire")]
            public string Code { get; set; }

            [PlayServJsonName("display_name")]
            public string DisplayName { get; set; }

            [PlayServIgnore]
            public string Ignored { get; set; }
        }

        private sealed class QueryableInventoryItem
        {
            public string Id { get; set; }

            [PlayServField("code_wire")]
            public string Code { get; set; }

            [PlayServJsonName("display_name")]
            public string DisplayName { get; set; }

            [PlayServField("durability")]
            public int Durability { get; set; }

            [PlayServField("owner_relation")]
            public OwnerRelation OwnerRelation { get; set; }

            [PlayServIgnore]
            public string Ignored { get; set; }
        }

        private sealed class OwnerRelation
        {
            [PlayServField("relation_name")]
            public string Name { get; set; }
        }

        private sealed class NestedInventoryItem
        {
            [PlayServField("code_wire")]
            public string Code { get; set; }

            [PlayServField("guild_wire")]
            public GuildRelation Guild { get; set; }
        }

        private sealed class GuildRelation
        {
            [PlayServJsonName("guild_name")]
            public string Name { get; set; }

            [PlayServField("owner_wire")]
            public OwnerRelation Owner { get; set; }

            [PlayServIgnore]
            public OwnerRelation IgnoredOwner { get; set; }
        }

        private sealed class InventoryProjection
        {
            public string Code { get; set; }
        }

        private sealed class GameConfig
        {
            [PlayServJsonName("message")]
            public string Message { get; set; }
        }

        private sealed class FakeDataHttpClient : IPlayServRuntimeHttpClient
        {
            public Func<PlayServRuntimeDataRequest, CancellationToken, Task<PlayServRuntimeDataResponse>> DataHandler;
            private readonly Queue<Func<PlayServRuntimeDataResponse>> _responses =
                new Queue<Func<PlayServRuntimeDataResponse>>();
            private readonly Queue<Func<PlayServRuntimeDataResponse>> _catalogueResponses =
                new Queue<Func<PlayServRuntimeDataResponse>>();

            public List<PlayServRuntimeDataRequest> Requests { get; } =
                new List<PlayServRuntimeDataRequest>();

            public List<PlayServRuntimeDataRequest> AllRequests { get; } =
                new List<PlayServRuntimeDataRequest>();

            public void EnqueueJson(int status, string body, string etag = null, string location = null) =>
                _responses.Enqueue(() => new PlayServRuntimeDataResponse(status, body, etag, location));

            public void EnqueueException(Exception exception) =>
                _responses.Enqueue(() => throw exception);

            public void EnqueueCatalogueJson(string body) =>
                _catalogueResponses.Enqueue(() =>
                    new PlayServRuntimeDataResponse(200, body, null, null));

            public void ConfigureDefaultCatalogue(string entityId, string name, bool singleton)
            {
                if (_catalogueResponses.Count > 0)
                    return;
                EnqueueCatalogueJson(
                    "{\"data\":[{\"entity_id\":\"" + entityId +
                    "\",\"name\":\"" + name +
                    "\",\"singleton\":" + (singleton ? "true" : "false") + "}]}");
            }

            public Task<PlayServRuntimeDataResponse> SendDataAsync(
                PlayServRuntimeDataRequest request,
                CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                AllRequests.Add(request);
                if (request.RelativePath == "data/tables" && _catalogueResponses.Count > 0)
                    return Task.FromResult(_catalogueResponses.Dequeue()());
                Requests.Add(request);
                if (DataHandler != null) return DataHandler(request, ct);
                return Task.FromResult(_responses.Dequeue()());
            }

            public Task<string> GetLatestVersionAsync(string gameId, CancellationToken ct = default) =>
                throw new NotSupportedException();

            public Task<PlayerTokenBundleDto> SignInAnonAsync(string clientToken, CancellationToken ct = default) =>
                throw new NotSupportedException();

            public Task<PlayerRefreshResponseDto> RefreshAsync(
                string clientToken,
                string refreshToken,
                CancellationToken ct = default) =>
                throw new NotSupportedException();

            public Task<PlayerTokenBundleDto> LoginExternalAsync(
                string clientToken,
                PlayerExternalLoginRequestDto request,
                string playerAccessToken = null,
                CancellationToken ct = default) =>
                throw new NotSupportedException();

            public Task SignOutAsync(
                string clientToken,
                string refreshToken,
                CancellationToken ct = default) =>
                throw new NotSupportedException();
        }

        private sealed class FakeSharedCollection<T> : ISharedCollection<T>
        {
            public IReadOnlyList<T> Items { get; } = Array.Empty<T>();

            public PlayServSubscriptionState State => PlayServSubscriptionState.Active;

            public PlayServError TerminalError => null;

            public event Action<IReadOnlyList<T>> Changed;
            public event Action<Playserv.DataSubscription.Exceptions.DataSubscriptionException> Error;
            public event Action<PlayServError> Failure;
            public event Action Terminated;

            public Task RefreshAsync(CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            }

            public Task<PlayServSubscriptionCloseResult> CloseAsync(CancellationToken ct = default) =>
                Task.FromResult(PlayServSubscriptionCloseResult.Success());

            public void Dispose()
            {
            }
        }

        private sealed class FakeCommandBus : IPlayServCommandBus
        {
            public PlayServState State => PlayServState.Offline;

            public IDisposable On<T>(Action<T> onNext) => EmptyDisposable.Instance;

            public IDisposable OnCommand(string commandName, Action<object> onNext) => EmptyDisposable.Instance;

            public Task SendAsync<T>(T command, string moduleName = null) => Task.CompletedTask;
        }

        private sealed class RecordSubscriptionCommandBus : IPlayServCommandBus
        {
            private readonly Dictionary<Type, List<Delegate>> _listeners =
                new Dictionary<Type, List<Delegate>>();
            private readonly long _subscriptionId = 71;

            public PlayServState State => PlayServState.Online;

            public int OpenCount { get; private set; }

            public int CloseCount { get; private set; }

            public DataSubscriptionRequest LastOpen { get; private set; }

            public int RefreshCount { get; private set; }

            public DataSubscriptionRefreshRequest LastRefresh { get; private set; }

            public object InitialRecordData { get; set; }

            public object RefreshRecordData { get; set; }

            public IDisposable On<T>(Action<T> onNext)
            {
                if (!_listeners.TryGetValue(typeof(T), out var values))
                {
                    values = new List<Delegate>();
                    _listeners[typeof(T)] = values;
                }
                values.Add(onNext);
                return new CallbackDisposable(() => values.Remove(onNext));
            }

            public IDisposable OnCommand(string commandName, Action<object> onNext) =>
                EmptyDisposable.Instance;

            public Task SendAsync<T>(T command, string moduleName = null)
            {
                if (command is DataSubscriptionRequest open)
                {
                    OpenCount++;
                    LastOpen = open;
                    Emit(new DataSubscriptionResponse
                    {
                        RequestId = open.RequestId,
                        Result = new DataSubscriptionResult { SubscriptionId = _subscriptionId }
                    });
                    Emit(new DataSubscriptionUpdate
                    {
                        DataSubscriptionId = _subscriptionId,
                        UpdateType = "Overwrite",
                        IsCollection = false,
                        Data = InitialRecordData
                    });
                }
                else if (command is DataSubscriptionCloseRequest close)
                {
                    CloseCount++;
                    Emit(new DataSubscriptionCloseResponse
                    {
                        RequestId = close.RequestId,
                        Result = new DataSubscriptionCloseResult { Success = true }
                    });
                }
                else if (command is DataSubscriptionRefreshRequest refresh)
                {
                    RefreshCount++;
                    LastRefresh = refresh;
                    Emit(new DataSubscriptionUpdate
                    {
                        RequestId = refresh.RequestId,
                        DataSubscriptionId = refresh.SubscriptionId,
                        UpdateType = "Overwrite",
                        IsCollection = false,
                        Data = RefreshRecordData ?? InitialRecordData
                    });
                }
                return Task.CompletedTask;
            }

            public void EmitRecordUpdate(object data)
            {
                Emit(new DataSubscriptionUpdate
                {
                    DataSubscriptionId = _subscriptionId,
                    UpdateType = "Overwrite",
                    IsCollection = false,
                    Data = data
                });
            }

            public void EmitTermination(int errorCode, string message)
            {
                Emit(new DataSubscriptionUpdate
                {
                    DataSubscriptionId = _subscriptionId,
                    UpdateType = "Terminated",
                    IsCollection = false,
                    ErrorCode = errorCode,
                    Message = message
                });
            }

            private void Emit<T>(T value)
            {
                if (!_listeners.TryGetValue(typeof(T), out var values))
                    return;
                foreach (var listener in values.ToArray())
                    ((Action<T>)listener)(value);
            }
        }

        private sealed class CallbackDisposable : IDisposable
        {
            private Action _dispose;

            public CallbackDisposable(Action dispose) => _dispose = dispose;

            public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
        }

        private sealed class FakeLogger : ILogger
        {
            public void Log(string message)
            {
            }

            public void LogWarning(string message)
            {
            }

            public void LogError(string message)
            {
            }
        }

        private sealed class EmptyDisposable : IDisposable
        {
            public static readonly EmptyDisposable Instance = new EmptyDisposable();

            public void Dispose()
            {
            }
        }
    }
}
