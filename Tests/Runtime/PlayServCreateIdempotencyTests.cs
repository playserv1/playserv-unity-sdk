using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.Data;
using Playserv.Http.Interfaces;
using UnityEngine.Networking;

namespace Playserv.Tests.Runtime
{
    public sealed partial class PlayServTypedRecordsTests
    {
        [Test]
        public void Create_http_header_preserves_opaque_key_without_normalizing()
        {
            // Exercise the real Unity header builder used by both data HTTP paths, without
            // sending a network request or widening the SDK's public testing surface.
            var type = typeof(IPlayServRuntimeHttpClient).Assembly.GetType(
                "Playserv.Http.Modules.Unity.UnityWebRequestRuntimeHttpClient", true);
            var applyHeaders = type.GetMethod("ApplyRuntimeRequestHeaders", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(applyHeaders, Is.Not.Null);
            using (var request = new UnityWebRequest("https://records.test", "POST"))
            {
                applyHeaders.Invoke(null, new object[]
                {
                    request, new PlayServRuntimeDataRequest { IdempotencyKey = " Purchase-42 " }, "pk_test"
                });
                Assert.That(request.GetRequestHeader("Idempotency-Key"), Is.EqualTo(" Purchase-42 "));
                Assert.That(request.GetRequestHeader("X-Playserv-Client"), Is.EqualTo("pk_test"));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Create_idempotency_key_is_exact_and_optional(bool bulk)
        {
            var fake = new FakeDataHttpClient();
            var set = CreateSet<InventoryItem>(fake, "ent_inventory");
            var value = new InventoryItem { Code = "sword", DisplayName = "Sword" };
            fake.DataHandler = (request, ct) => Task.FromResult(new PlayServRuntimeDataResponse(
                201, bulk ? "{\"ids\":[\"rec_1\"],\"created\":1}" : RecordJson("rec_1", "sword", "Sword"), null, null));

            // A maximum-length key is forwarded unchanged, including case and punctuation.
            var supplied = "Purchase-42:" + new string('X', 116);
            Func<string, Task> create = key => bulk
                ? (Task)set.BulkCreateAsync(new[] { value }, idempotencyKey: key)
                : set.CreateAsync(value, idempotencyKey: key);
            create(supplied).GetAwaiter().GetResult();
            create(null).GetAwaiter().GetResult();
            create(null).GetAwaiter().GetResult();

            Assert.That(fake.Requests, Has.Count.EqualTo(3));
            Assert.That(fake.Requests[0].IdempotencyKey, Is.EqualTo(supplied));
            Assert.That(fake.Requests[0].Method, Is.EqualTo("POST"));
            Assert.That(fake.Requests[0].RelativePath, Is.EqualTo(
                "data/tables/ent_inventory/" + (bulk ? "records:bulk-create" : "records")));
            Assert.That(fake.Requests[0].JsonBody, Does.Not.Contain(supplied));
            Assert.That(Guid.TryParse(fake.Requests[1].IdempotencyKey, out _), Is.True);
            Assert.That(Guid.TryParse(fake.Requests[2].IdempotencyKey, out _), Is.True);
            Assert.That(fake.Requests[1].IdempotencyKey, Is.Not.EqualTo(fake.Requests[2].IdempotencyKey));
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void Create_manual_retry_replays_after_lost_response_or_cancellation(bool bulk, bool cancel)
        {
            var fake = new FakeDataHttpClient();
            var set = CreateSet<InventoryItem>(fake, "ent_inventory");
            var values = new[] { new InventoryItem { Code = "sword", DisplayName = "Sword" } };
            var defaults = new Dictionary<string, object> { ["rarity"] = "common" };
            var accepted = new Dictionary<string, string>();
            var created = 0;
            using (var cts = new CancellationTokenSource())
            {
                // A small server fixture: commit before losing the first response. A replay
                // keeps body/status but has no ETag header, like the backend idempotency filter.
                fake.DataHandler = (request, ct) =>
                {
                    var scope = request.ClientToken + ":" + request.BearerToken + ":" +
                        request.Method + ":" + request.RelativePath + ":" + request.IdempotencyKey;
                    if (accepted.TryGetValue(scope, out var body))
                    {
                        if (body != request.JsonBody)
                            throw HttpError(409, "conflict", "{\"code\":\"conflict\"}");
                    }
                    else
                    {
                        accepted.Add(scope, request.JsonBody);
                        created++;
                        if (created == 1)
                        {
                            if (cancel)
                            {
                                cts.Cancel();
                                ct.ThrowIfCancellationRequested();
                            }
                            throw new TimeoutException("Simulated lost response after commit.");
                        }
                    }
                    return Task.FromResult(new PlayServRuntimeDataResponse(201,
                        bulk ? "{\"ids\":[\"rec_committed\"],\"created\":1}" : RecordJson("rec_committed", "sword", "Sword"),
                        null, null));
                };

                Func<CancellationToken, Task> create = ct => bulk
                    ? (Task)set.BulkCreateAsync(values, defaults, ct, "purchase-42")
                    : set.CreateAsync(values[0], ct, "purchase-42");
                if (cancel)
                    CaptureExceptionAsync<OperationCanceledException>(() => create(cts.Token)).GetAwaiter().GetResult();
                else
                {
                    var failure = Assert.Throws<PlayServDataException>(() => create(cts.Token).GetAwaiter().GetResult());
                    Assert.That(failure.UnifiedError.SourceCode, Is.EqualTo("transport_error"));
                }
                Assert.That(fake.Requests, Has.Count.EqualTo(1), "No automatic retry is allowed.");

                // Two explicit retries both recover the same committed record IDs.
                for (var retry = 0; retry < 2; retry++)
                {
                    if (bulk)
                    {
                        var result = set.BulkCreateAsync(values, defaults, idempotencyKey: "purchase-42").GetAwaiter().GetResult();
                        Assert.That(result.RecordIds, Is.EqualTo(new[] { "rec_committed" }));
                        Assert.That(result.CreatedCount, Is.EqualTo(1));
                    }
                    else
                    {
                        var record = set.CreateAsync(values[0], idempotencyKey: "purchase-42").GetAwaiter().GetResult();
                        Assert.That(record.Id, Is.EqualTo("rec_committed"));
                        Assert.That(record.Value.Code, Is.EqualTo("sword"));
                        Assert.That(record.ETag, Is.Not.Null.And.Not.Empty);
                        Assert.That(record.HasPendingChanges, Is.False);
                    }
                }
                Assert.That(created, Is.EqualTo(1));
                Assert.That(fake.Requests, Has.Count.EqualTo(3));
                Assert.That(fake.Requests.Select(request => request.JsonBody).Distinct().Count(), Is.EqualTo(1));

                // Reusing the key for another payload remains a typed backend conflict.
                if (bulk) defaults["rarity"] = "rare";
                else values[0].DisplayName = "Changed";
                var conflict = Assert.Throws<PlayServRecordConflictException>(() => create(default).GetAwaiter().GetResult());
                Assert.That(conflict.UnifiedError.SourceCode, Is.EqualTo("conflict"));
                Assert.That(conflict.Kind, Is.EqualTo(PlayServRecordConflictKind.Unknown));
                Assert.That(fake.Requests, Has.Count.EqualTo(4));
                Assert.That(created, Is.EqualTo(1));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Create_invalid_key_and_precancellation_fail_before_catalogue(bool bulk)
        {
            // No explicit ID and no queued responses: neither catalogue nor mutations may run.
            var fake = new FakeDataHttpClient();
            var set = CreateSet<InventoryItem>(fake, null);
            var value = new InventoryItem { Code = "sword" };
            Func<string, CancellationToken, Task> create = (key, ct) => bulk
                ? (Task)set.BulkCreateAsync(new[] { value }, ct: ct, idempotencyKey: key)
                : set.CreateAsync(value, ct: ct, idempotencyKey: key);
            foreach (var key in new[] { "", "   ", "bad\r\nheader", "bad\tkey", "bad\0key", "bad\u007fkey", new string('x', 129) })
            {
                var error = Assert.Throws<ArgumentException>(() => create(key, default).GetAwaiter().GetResult());
                Assert.That(error.ParamName, Is.EqualTo("idempotencyKey"));
                Assert.That(error.Message, Does.Not.Contain("bad"));
            }
            CaptureExceptionAsync<OperationCanceledException>(() =>
                create("valid-key", new CancellationToken(true))).GetAwaiter().GetResult();
            Assert.That(fake.AllRequests, Is.Empty);
        }

        [Test]
        public void Create_legacy_positional_named_default_and_method_group_calls_remain_compatible()
        {
            var fake = new FakeDataHttpClient();
            var set = CreateSet<InventoryItem>(fake, "ent_inventory");
            var value = new InventoryItem { Code = "sword" };
            var values = new[] { value };
            fake.DataHandler = (request, ct) => Task.FromResult(new PlayServRuntimeDataResponse(201,
                request.RelativePath.EndsWith("records:bulk-create", StringComparison.Ordinal)
                    ? "{\"ids\":[\"rec_1\"],\"created\":1}" : RecordJson("rec_1", "sword", "Sword"), null, null));

            Func<InventoryItem, CancellationToken, Task<PlayServRecord<InventoryItem>>> create = set.CreateAsync;
            Func<IEnumerable<InventoryItem>, IReadOnlyDictionary<string, object>, CancellationToken,
                Task<PlayServBulkCreateResult>> bulkCreate = set.BulkCreateAsync;
            set.CreateAsync(value).GetAwaiter().GetResult();
            set.CreateAsync(value, default).GetAwaiter().GetResult();
            set.CreateAsync(value, ct: default).GetAwaiter().GetResult();
            create(value, default).GetAwaiter().GetResult();
            set.BulkCreateAsync(values).GetAwaiter().GetResult();
            set.BulkCreateAsync(values, null).GetAwaiter().GetResult();
            set.BulkCreateAsync(values, default, default).GetAwaiter().GetResult();
            set.BulkCreateAsync(values, ct: default).GetAwaiter().GetResult();
            set.BulkCreateAsync(values, defaults: null, ct: default).GetAwaiter().GetResult();
            bulkCreate(values, null, default).GetAwaiter().GetResult();

            Assert.That(fake.Requests, Has.Count.EqualTo(10));
            Assert.That(fake.Requests.Select(request => request.IdempotencyKey).Distinct().Count(), Is.EqualTo(10));
        }
    }
}
