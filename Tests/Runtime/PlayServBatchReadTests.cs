using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.Data;
using Playserv.Http.Interfaces;
using UnityEngine.TestTools;

namespace Playserv.Tests.Runtime
{
    public sealed partial class PlayServTypedRecordsTests
    {
        [Test]
        public void Batch_reads_deduplicate_wire_ids_and_return_independent_ordered_handles()
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueJson(200, QueryPageJson(RecordJson("rec_b", "b", "B"), RecordJson("rec_a", "a", "A")));
            var set = CreateSet<InventoryItem>(fake, "ent_inventory");
            var result = set.LoadManyAsync(new[] { "rec_a", "rec_b", "rec_a" }).GetAwaiter().GetResult();
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Items.Select(x => x.Value.Id), Is.EqualTo(new[] { "rec_a", "rec_b", "rec_a" }));
            Assert.That(fake.Requests, Has.Count.EqualTo(1));
            Assert.That(fake.Requests[0].RelativePath, Does.EndWith("/records:query"));
            Assert.That(fake.Requests[0].JsonBody, Does.Contain("\"field\":\"id\",\"op\":\"in\",\"value\":[\"rec_a\",\"rec_b\"]"));
            var first = result.Items[0].Value;
            var duplicate = result.Items[2].Value;
            Assert.That(first, Is.Not.SameAs(duplicate));
            Assert.That(first.Value, Is.Not.SameAs(duplicate.Value));
            Assert.That(first.ETag, Is.Not.Empty);
            Assert.That(first.ETag, Is.EqualTo(duplicate.ETag));
            first.Value.DisplayName = "changed";
            Assert.That(duplicate.Value.DisplayName, Is.EqualTo("A"));
        }

        [Test]
        public void Batch_reads_follow_cursor_pages_and_keep_other_chunks_after_failure()
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueJson(200, QueryPageWithCursorJson("next", true, RecordJson("rec_0", "0", "Zero")));
            fake.EnqueueJson(200, QueryPageJson(RecordJson("rec_199", "199", "Last")));
            fake.EnqueueException(HttpError(503, "unavailable", "{}"));
            var set = CreateSet<InventoryItem>(fake, "ent_inventory");
            var result = set.LoadManyAsync(Enumerable.Range(0, 201).Select(i => "rec_" + i), maxConcurrency: 1).GetAwaiter().GetResult();
            Assert.That(result.SucceededCount, Is.EqualTo(2));
            Assert.That(result.Items[200].Error.SourceCode, Is.EqualTo("unavailable"));
            Assert.That(result.Items[1].Exception, Is.TypeOf<PlayServRecordNotFoundException>());
            Assert.That(fake.Requests, Has.Count.EqualTo(3));
            Assert.That(fake.Requests[1].JsonBody, Does.Contain("\"cursor\":\"next\""));
            Assert.That(fake.Requests[2].JsonBody, Does.Contain("\"value\":[\"rec_200\"]"));
        }

        [TestCase("{}")]
        [TestCase("{\"data\":[],\"page\":{\"has_more\":true}}")]
        [TestCase("{\"data\":[{\"id\":\"unexpected\"}],\"page\":{\"has_more\":false}}")]
        public void Batch_reads_reject_malformed_or_inconsistent_pages(string body)
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueJson(200, body);
            var set = CreateSet<InventoryItem>(fake, "ent_inventory");
            var result = set.LoadManyAsync(new[] { "rec_1" }).GetAwaiter().GetResult();
            Assert.That(result.Items[0].Error.SourceCode, Is.EqualTo("invalid_response"));
        }

        [Test]
        public void Batch_reads_keep_projection_point_loads_and_validate_before_io()
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueJson(200, RecordJson("rec_1", "one", "One"), "\"etag\"");
            var set = CreateSet<InventoryItem>(fake, "ent_inventory");
            Assert.Throws<ArgumentOutOfRangeException>(() => set.LoadManyAsync(new[] { "rec_1" }, maxConcurrency: 0).GetAwaiter().GetResult());
            Assert.Throws<OperationCanceledException>(() => set.LoadManyAsync(new[] { "rec_1" }, ct: new CancellationToken(true)).GetAwaiter().GetResult());
            Assert.That(set.LoadManyAsync(Array.Empty<string>()).GetAwaiter().GetResult().Items, Is.Empty);
            Assert.That(fake.AllRequests, Is.Empty);
            var result = set.LoadManyAsync(new[] { "rec_1" }, new PlayServLoadOptions { Fields = new[] { "code_wire" } }).GetAwaiter().GetResult();
            Assert.That(result.Items[0].Value.IsPartial, Is.True);
            Assert.That(result.Items[0].Value.ETag, Is.EqualTo("\"etag\""));
            Assert.That(fake.Requests.Single().Method, Is.EqualTo("GET"));
            Assert.That(fake.Requests.Single().RelativePath, Does.EndWith("/records/rec_1?fields=code_wire"));
        }

        [UnityTest]
        public IEnumerator Batch_reads_bound_inflight_chunks_and_cancel_without_returning_partial_success()
        {
            var fake = new FakeDataHttpClient();
            var pending = new List<TaskCompletionSource<PlayServRuntimeDataResponse>>();
            fake.DataHandler = (request, token) =>
            {
                var completion = new TaskCompletionSource<PlayServRuntimeDataResponse>();
                token.Register(() => completion.TrySetCanceled());
                pending.Add(completion);
                return completion.Task;
            };
            var set = CreateSet<InventoryItem>(fake, "ent_inventory");
            using var cancellation = new CancellationTokenSource();
            var task = set.LoadManyAsync(Enumerable.Range(0, 601).Select(i => "rec_" + i), maxConcurrency: 2, ct: cancellation.Token);
            Assert.That(pending, Has.Count.EqualTo(2));
            cancellation.Cancel();
            var deadline = UnityEngine.Time.realtimeSinceStartup + 5;
            while (!task.IsCompleted && UnityEngine.Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(task.IsCompleted, Is.True);
            Assert.Catch<OperationCanceledException>(() => task.GetAwaiter().GetResult());
            Assert.That(pending, Has.Count.EqualTo(2));
        }
    }
}
