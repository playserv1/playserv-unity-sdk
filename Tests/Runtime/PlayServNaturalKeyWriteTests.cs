using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Playserv.Data;
using Playserv.Http.Interfaces;

namespace Playserv.Tests.Runtime
{
    public sealed partial class PlayServTypedRecordsTests
    {
        [TestCase(PlayServUpsertMode.Seed, "seed", true)]
        [TestCase(PlayServUpsertMode.Managed, "managed", false)]
        public void Natural_upsert_is_one_native_write_without_lookup_or_hydration(
            PlayServUpsertMode mode, string wireMode, bool created)
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueJson(created ? 201 : 200, "{\"id\":\"rec_result\",\"created\":" + created.ToString().ToLowerInvariant() + "}");
            var set = CreateSet<InventoryItem>(fake, "ent_inventory");
            var result = set.UpsertByNaturalKeyAsync(
                PlayServNaturalKey<InventoryItem>.For(x => x.Code, "sword"),
                new { display_name = "Sword" }, mode, "\"v1\"", "seed-42").GetAwaiter().GetResult();
            Assert.That(result.Id, Is.EqualTo("rec_result"));
            Assert.That(result.Created, Is.EqualTo(created));
            Assert.That(fake.Requests, Has.Count.EqualTo(1));
            var request = fake.Requests.Single();
            Assert.That(request.Method, Is.EqualTo("POST"));
            Assert.That(request.RelativePath, Is.EqualTo("data/tables/ent_inventory/records:upsert"));
            Assert.That(request.JsonBody, Does.Contain("\"field\":\"code_wire\""));
            Assert.That(request.JsonBody, Does.Contain("\"value\":\"sword\""));
            Assert.That(request.JsonBody, Does.Contain("\"mode\":\"" + wireMode + "\""));
            Assert.That(request.JsonBody, Does.Contain("\"record\":{\"display_name\":\"Sword\"}"));
            Assert.That(request.IfMatch, Is.EqualTo("\"v1\""));
            Assert.That(request.IdempotencyKey, Is.EqualTo("seed-42"));
        }

        [Test]
        public void Bulk_upsert_preserves_row_order_defaults_and_atomic_acknowledgments()
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueJson(200, "{\"rows\":[{\"id\":\"rec_2\",\"created\":false},{\"id\":\"rec_1\",\"created\":true}],\"created\":1,\"updated\":1}");
            var set = CreateSet<InventoryItem>(fake, "ent_inventory");
            var result = set.BulkUpsertAsync(new[]
            {
                new PlayServBulkUpsertRow<InventoryItem>(PlayServNaturalKey<InventoryItem>.For(x => x.Code, "sword"),
                    new InventoryItem { Code = "sword", DisplayName = "Sword" }),
                new PlayServBulkUpsertRow<InventoryItem>(PlayServNaturalKey<InventoryItem>.For("code_wire", "shield"),
                    new Dictionary<string, object> { ["display_name"] = "Shield" })
            }, PlayServUpsertMode.Managed, new { rarity = "common" }, "batch-42").GetAwaiter().GetResult();
            Assert.That(result.Rows.Select(row => row.Id), Is.EqualTo(new[] { "rec_2", "rec_1" }));
            Assert.That(result.Created, Is.EqualTo(1));
            Assert.That(result.Updated, Is.EqualTo(1));
            Assert.That(fake.Requests, Has.Count.EqualTo(1));
            Assert.That(fake.Requests[0].RelativePath, Does.EndWith("/records:bulk-upsert"));
            Assert.That(fake.Requests[0].IdempotencyKey, Is.EqualTo("batch-42"));
            Assert.That(fake.Requests[0].JsonBody, Does.Contain("\"defaults\":{\"rarity\":\"common\"}"));
            Assert.That(fake.Requests[0].JsonBody, Does.Contain("\"records\":["));
        }

        [Test]
        public void Natural_patch_and_delete_escape_key_and_preserve_etag()
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueJson(200, RecordJson("rec_1", "a & b", "New"), "\"v2\"");
            fake.EnqueueJson(204, null);
            var set = CreateSet<InventoryItem>(fake, "ent_inventory");
            var key = PlayServNaturalKey<InventoryItem>.For(x => x.Code, "a & b");
            var result = set.PatchByNaturalKeyAsync(key, new { display_name = "New" }, "\"v1\"").GetAwaiter().GetResult();
            set.DeleteByNaturalKeyAsync(key, result.ETag).GetAwaiter().GetResult();
            Assert.That(result.Value.DisplayName, Is.EqualTo("New"));
            Assert.That(result.ETag, Is.EqualTo("\"v2\""));
            Assert.That(result.HasPendingChanges, Is.False);
            Assert.That(fake.Requests.Select(x => x.Method), Is.EqualTo(new[] { "PATCH", "DELETE" }));
            Assert.That(fake.Requests.All(x => x.RelativePath == "data/tables/ent_inventory/records:by-natural-key?field=code_wire&value=a%20%26%20b"), Is.True);
            Assert.That(fake.Requests.Select(x => x.IfMatch), Is.EqualTo(new[] { "\"v1\"", "\"v2\"" }));
        }

        [Test]
        public void Natural_writes_validate_before_catalogue_and_observe_cancellation()
        {
            var fake = new FakeDataHttpClient();
            var set = CreateSet<InventoryItem>(fake, "ent_inventory");
            var key = PlayServNaturalKey<InventoryItem>.For(x => x.Code, "sword");
            Assert.Throws<ArgumentOutOfRangeException>(() => set.UpsertByNaturalKeyAsync(key, new { }, (PlayServUpsertMode)20).GetAwaiter().GetResult());
            Assert.Throws<InvalidOperationException>(() => set.PatchByNaturalKeyAsync(key, new[] { 1 }).GetAwaiter().GetResult());
            Assert.Throws<ArgumentException>(() => set.DeleteByNaturalKeyAsync(key, "bad\r\nheader").GetAwaiter().GetResult());
            Assert.Throws<ArgumentOutOfRangeException>(() => set.BulkUpsertAsync(Array.Empty<PlayServBulkUpsertRow<InventoryItem>>(), PlayServUpsertMode.Seed).GetAwaiter().GetResult());
            var row = new PlayServBulkUpsertRow<InventoryItem>(key, new { });
            Assert.Throws<ArgumentOutOfRangeException>(() => set.BulkUpsertAsync(Enumerable.Repeat(row, 201), PlayServUpsertMode.Seed).GetAwaiter().GetResult());
            Assert.Throws<OperationCanceledException>(() => set.UpsertByNaturalKeyAsync(key, new { }, PlayServUpsertMode.Seed, ct: new CancellationToken(true)).GetAwaiter().GetResult());
            Assert.That(fake.AllRequests, Is.Empty);
        }

        [TestCase("{}")]
        [TestCase("{broken")]
        [TestCase("{\"id\":\"rec_1\",\"created\":\"false\"}")]
        [TestCase("")]
        public void Natural_upsert_rejects_incomplete_acknowledgment(string body)
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueJson(200, body);
            var set = CreateSet<InventoryItem>(fake, "ent_inventory");
            var error = Assert.Throws<PlayServDataException>(() => set.UpsertByNaturalKeyAsync(
                PlayServNaturalKey<InventoryItem>.For(x => x.Code, "sword"), new { }, PlayServUpsertMode.Seed).GetAwaiter().GetResult());
            Assert.That(error.UnifiedError.SourceCode, Is.EqualTo("invalid_response"));
            Assert.That(fake.Requests.Count, Is.EqualTo(1));
        }

        [Test]
        public void Natural_write_acl_denial_remains_typed()
        {
            var fake = new FakeDataHttpClient();
            var denied = AclCatalogueJson("ent_inventory", "InventoryItem", false, "owner", true, false);
            fake.EnqueueCatalogueJson(denied);
            fake.EnqueueCatalogueJson(denied);
            var set = CreateSet<InventoryItem>(fake, "ent_inventory");
            var key = PlayServNaturalKey<InventoryItem>.For(x => x.Code, "sword");
            Assert.Throws<PlayServDataAccessDeniedException>(() => set.UpsertByNaturalKeyAsync(key, new { }, PlayServUpsertMode.Seed).GetAwaiter().GetResult());
            Assert.That(fake.Requests, Is.Empty);
        }

        [TestCase(404, "record_not_found", typeof(PlayServRecordNotFoundException))]
        [TestCase(412, "precondition_failed", typeof(PlayServRecordConflictException))]
        public void Natural_write_backend_failures_are_not_retried(int status, string code, Type expected)
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueException(HttpError(status, code, "{\"code\":\"" + code + "\"}"));
            var set = CreateSet<InventoryItem>(fake, "ent_inventory");
            var error = Assert.Throws(expected, () => set.DeleteByNaturalKeyAsync(
                PlayServNaturalKey<InventoryItem>.For(x => x.Code, "sword"), "\"v1\"").GetAwaiter().GetResult());
            Assert.That(((PlayServDataException)error).UnifiedError.SourceCode, Is.EqualTo(code));
            Assert.That(fake.Requests, Has.Count.EqualTo(1));
        }
    }
}
