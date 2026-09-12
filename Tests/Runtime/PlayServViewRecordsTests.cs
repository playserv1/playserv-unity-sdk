using System;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Playserv.Data;
using Playserv.Wrapper;

namespace Playserv.Tests.Runtime
{
    public sealed partial class PlayServTypedRecordsTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void View_reads_use_exact_get_pagination_and_keep_order_without_hydration(bool explicitId)
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueCatalogueJson(AclCatalogueJson("ent_inventory", "InventoryItem", false, "owner", true, true));
            fake.EnqueueJson(200, QueryPageWithCursorJson("next +/&?", true,
                RecordJson("rec_2", "axe", null), RecordJson("rec_1", "sword", null)));
            fake.EnqueueJson(200, "{\"data\":[],\"page\":{\"has_more\":false,\"cursor_prev\":\"previous\"}}");
            var settings = Settings();
            settings.PlayerAccessToken = "player.jwt.value";
            var set = CreateSet<InventoryItem>(fake, explicitId ? "ent_inventory" : null, settings);
            var first = set.QueryViewAsync("view /?&=+", new PlayServPagination(limit: 200)).GetAwaiter().GetResult();
            var second = set.QueryViewAsync("view /?&=+", new PlayServPagination(first.NextCursor, 1)).GetAwaiter().GetResult();

            Assert.That(first.Records.Select(record => record.Id), Is.EqualTo(new[] { "rec_2", "rec_1" }));
            Assert.That(first.Records.All(record => record.IsPartial), Is.True);
            Assert.That(first.HasMore, Is.True);
            Assert.That(first.TotalEstimate, Is.EqualTo(2));
            Assert.That(second.Records, Is.Empty);
            Assert.That(second.HasMore, Is.False);
            Assert.That(second.PreviousCursor, Is.EqualTo("previous"));
            Assert.That(second.TotalEstimate, Is.Null);
            Assert.That(fake.AllRequests.Count, Is.EqualTo(3), "One catalogue lookup and two pages; no per-record hydration.");
            Assert.That(fake.Requests[0].Method, Is.EqualTo("GET"));
            Assert.That(fake.Requests[0].ClientToken, Is.EqualTo("pk_test"));
            Assert.That(fake.Requests[0].BearerToken, Is.EqualTo("player.jwt.value"));
            Assert.That(fake.Requests[0].RelativePath, Is.EqualTo("data/tables/ent_inventory/records?view_id=view%20%2F%3F%26%3D%2B&limit=200"));
            Assert.That(fake.Requests[1].RelativePath, Is.EqualTo("data/tables/ent_inventory/records?view_id=view%20%2F%3F%26%3D%2B&limit=1&cursor=next%20%2B%2F%26%3F"));
            Assert.That(fake.Requests.All(request => request.JsonBody == null && request.IdempotencyKey == null && request.IfMatch == null), Is.True);
        }

        [Test]
        public void View_handles_require_explicit_reload_before_save()
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueJson(200, QueryPageJson("{\"id\":\"rec_1\",\"code_wire\":\"sword\"}"));
            var set = CreateSet<InventoryItem>(fake, "ent_inventory");
            var page = set.QueryViewAsync("view-public").GetAwaiter().GetResult();
            var record = page.Records[0];
            Assert.That(record.Value.DisplayName, Is.Null);
            Assert.That(record.IsPartial, Is.True);
            Assert.That(fake.Requests[0].RelativePath, Is.EqualTo("data/tables/ent_inventory/records?view_id=view-public&limit=50"));
            record.Value.DisplayName = "Must not write a partial DTO";
            Assert.Throws<InvalidOperationException>(() => record.SaveAsync().GetAwaiter().GetResult());
            Assert.That(fake.Requests.Count, Is.EqualTo(1));

            fake.EnqueueJson(200, RecordJson("rec_1", "sword", "Full name"), "\"full\"");
            record.ReloadAsync().GetAwaiter().GetResult();
            Assert.That(record.IsPartial, Is.False);
            Assert.That(record.Value.DisplayName, Is.EqualTo("Full name"));
            record.Value.DisplayName = "New name";
            fake.EnqueueJson(200, RecordJson("rec_1", "sword", "New name"), "\"saved\"");
            record.SaveAsync().GetAwaiter().GetResult();
            Assert.That(fake.Requests[1].RelativePath, Is.EqualTo("data/tables/ent_inventory/records/rec_1"));
            Assert.That(fake.Requests[2].Method, Is.EqualTo("PATCH"));
            Assert.That(fake.Requests[2].IfMatch, Is.EqualTo("\"full\""));
            Assert.That(record.HasPendingChanges, Is.False);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("  ")]
        [TestCase("view\nsecret")]
        public void Invalid_view_id_fails_before_catalogue(string viewId)
        {
            var fake = new FakeDataHttpClient();
            var set = CreateSet<InventoryItem>(fake, "ent_inventory");
            Assert.Throws<ArgumentException>(() => set.QueryViewAsync(viewId).GetAwaiter().GetResult());
            Assert.That(fake.AllRequests, Is.Empty);
        }

        [TestCase(0)]
        [TestCase(201)]
        public void Invalid_view_limit_fails_before_catalogue(int limit)
        {
            var fake = new FakeDataHttpClient();
            var set = CreateSet<InventoryItem>(fake, "ent_inventory");
            Assert.Throws<ArgumentOutOfRangeException>(() => set.QueryViewAsync("view", new PlayServPagination(limit: limit)).GetAwaiter().GetResult());
            Assert.That(fake.AllRequests, Is.Empty);
        }

        [Test]
        public void View_cancellation_before_and_during_request_remains_cancellation()
        {
            var fake = new FakeDataHttpClient();
            var set = CreateSet<InventoryItem>(fake, "ent_inventory");
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            Assert.Throws<OperationCanceledException>(() => set.QueryViewAsync("view", ct: canceled.Token).GetAwaiter().GetResult());
            Assert.That(fake.AllRequests, Is.Empty);
            using var during = new CancellationTokenSource();
            fake.DataHandler = (request, token) =>
            {
                Assert.That(token, Is.EqualTo(during.Token));
                during.Cancel();
                token.ThrowIfCancellationRequested();
                throw new InvalidOperationException("unreachable");
            };
            Assert.Throws<OperationCanceledException>(() => set.QueryViewAsync("view", ct: during.Token).GetAwaiter().GetResult());
            Assert.That(fake.Requests.Count, Is.EqualTo(1));
        }

        [TestCase(404, "not_found")]
        [TestCase(400, "invalid_cursor")]
        [TestCase(400, "query_limit_exceeded")]
        public void View_errors_preserve_existing_records_mapping(int status, string code)
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueException(HttpError(status, code, "{}"));
            var set = CreateSet<InventoryItem>(fake, "ent_inventory");
            var error = CaptureExceptionAsync<PlayServDataException>(() => set.QueryViewAsync("missing-view")).GetAwaiter().GetResult();
            Assert.That(error.UnifiedError.SourceCode, Is.EqualTo(code));
            if (status == 404) Assert.That(error, Is.TypeOf<PlayServRecordNotFoundException>());
            Assert.That(fake.AllRequests.Count, Is.EqualTo(2));
        }

        [Test]
        public void View_acl_rejection_is_not_bypassed()
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueException(HttpError(403, "table_read_forbidden", "{\"code\":\"table_read_forbidden\"}"));
            var set = CreateSet<InventoryItem>(fake, "ent_inventory");
            var error = Assert.Throws<PlayServDataAccessDeniedException>(() => set.QueryViewAsync("view").GetAwaiter().GetResult());
            Assert.That(error.WasRejectedLocally, Is.False);
            Assert.That(error.Operation, Is.EqualTo(PlayServDataAccessOperation.Read));
            Assert.That(fake.Requests.Count, Is.EqualTo(1));

            var denied = new FakeDataHttpClient();
            denied.EnqueueCatalogueJson(AclCatalogueJson("ent_inventory", "InventoryItem", false, "public", false, false));
            denied.EnqueueCatalogueJson(AclCatalogueJson("ent_inventory", "InventoryItem", false, "public", false, false));
            var deniedSet = CreateSet<InventoryItem>(denied, "ent_inventory");
            var local = Assert.Throws<PlayServDataAccessDeniedException>(() => deniedSet.QueryViewAsync("view").GetAwaiter().GetResult());
            Assert.That(local.WasRejectedLocally, Is.True);
            Assert.That(denied.Requests, Is.Empty);
        }

        [TestCase("{}")]
        [TestCase("{\"data\":{},\"page\":{\"has_more\":false}}")]
        [TestCase("{\"data\":[],\"page\":{}}")]
        public void Malformed_view_page_does_not_become_an_empty_success(string body)
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueJson(200, body);
            var set = CreateSet<InventoryItem>(fake, "ent_inventory");
            var error = Assert.Throws<PlayServDataException>(() => set.QueryViewAsync("view").GetAwaiter().GetResult());
            Assert.That(error.UnifiedError.SourceCode, Is.EqualTo("invalid_response"));
            Assert.That(fake.Requests.Count, Is.EqualTo(1));
        }
    }
}
