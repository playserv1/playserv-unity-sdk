using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.Data;
using Playserv.Http.Interfaces;
using UnityEngine;
using UnityEngine.TestTools;

namespace Playserv.Tests.Runtime
{
    public sealed partial class PlayServTypedRecordsTests
    {
        [UnityTest]
        public IEnumerator Save_preserves_new_edits_and_uses_acknowledged_snapshot_for_next_patch()
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueJson(200, RecordJson("rec_1", "sword", "Old"), "\"v1\"");
            var record = CreateSet<InventoryItem>(fake, "ent_inventory").LoadAsync("rec_1").GetAwaiter().GetResult();
            var response = new TaskCompletionSource<PlayServRuntimeDataResponse>();
            fake.DataHandler = (_, __) => response.Task;
            record.Value.DisplayName = "Sent";
            var save = record.SaveAsync();
            Assert.That(save.IsCompleted, Is.False);
            Assert.That(fake.Requests.Last().JsonBody, Is.EqualTo("{\"display_name\":\"Sent\"}"));

            record.Value.DisplayName = "Typed while saving";
            response.SetResult(new PlayServRuntimeDataResponse(200,
                RecordJson("rec_1", "server-normalized-code", "Server normalized name", "2026-08-18T00:00:00Z"), "\"v2\"", null));
            yield return WaitForSaveTask(save);
            save.GetAwaiter().GetResult();

            Assert.That(record.Value.DisplayName, Is.EqualTo("Typed while saving"));
            Assert.That(record.Value.Code, Is.EqualTo("server-normalized-code"));
            Assert.That(record.ETag, Is.EqualTo("\"v2\""));
            Assert.That(record.UpdatedAt, Is.EqualTo(DateTimeOffset.Parse("2026-08-18T00:00:00Z")));
            Assert.That(record.CanonicalSnapshot, Does.Contain("Server normalized name"));
            Assert.That(record.CanonicalSnapshot, Does.Not.Contain("Typed while saving"));
            Assert.That(record.HasPendingChanges, Is.True);

            fake.DataHandler = null;
            fake.EnqueueJson(200, RecordJson("rec_1", "server-normalized-code", "Typed while saving"), "\"v3\"");
            record.SaveAsync().GetAwaiter().GetResult();
            Assert.That(fake.Requests.Last().IfMatch, Is.EqualTo("\"v2\""));
            Assert.That(fake.Requests.Last().JsonBody, Is.EqualTo("{\"display_name\":\"Typed while saving\"}"));
            Assert.That(record.HasPendingChanges, Is.False);
        }

        [UnityTest]
        public IEnumerator Save_retains_nested_collections_removals_and_explicit_null_as_pending()
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueJson(200,
                "{\"id\":\"rec_1\",\"title\":\"old\",\"profile\":{\"region\":\"eu\"}," +
                "\"tags\":[\"one\"],\"remove_me\":1,\"nullable\":\"old\"}", "\"v1\"");
            var record = CreateSet<Dictionary<string, object>>(fake, "ent_data").LoadAsync("rec_1").GetAwaiter().GetResult();
            var response = new TaskCompletionSource<PlayServRuntimeDataResponse>();
            fake.DataHandler = (_, __) => response.Task;
            record.Value["title"] = "sent";
            var save = record.SaveAsync();
            ((IDictionary<string, object>)record.Value["profile"])["region"] = "us";
            ((IList)record.Value["tags"]).Add("two");
            record.Value.Remove("remove_me");
            record.Value["nullable"] = null;
            record.Value["added"] = true;
            response.SetResult(new PlayServRuntimeDataResponse(200,
                "{\"id\":\"rec_1\",\"title\":\"server\",\"profile\":{\"region\":\"eu\"}," +
                "\"tags\":[\"one\"],\"remove_me\":1,\"nullable\":\"old\",\"server_only\":42}", "\"v2\"", null));
            yield return WaitForSaveTask(save);
            save.GetAwaiter().GetResult();
            Assert.That(record.Value["title"], Is.EqualTo("server"));
            Assert.That(record.Value["server_only"], Is.EqualTo(42));
            Assert.That(((IDictionary<string, object>)record.Value["profile"])["region"], Is.EqualTo("us"));
            Assert.That((IList)record.Value["tags"], Is.EqualTo(new[] { "one", "two" }));
            Assert.That(record.Value.ContainsKey("remove_me"), Is.False);
            Assert.That(record.Value["nullable"], Is.Null);
            Assert.That(record.Value["added"], Is.EqualTo(true));
            Assert.That(record.HasPendingChanges, Is.True);

            fake.DataHandler = null;
            fake.EnqueueJson(200,
                "{\"id\":\"rec_1\",\"title\":\"server\",\"profile\":{\"region\":\"us\"}," +
                "\"tags\":[\"one\",\"two\"],\"nullable\":null,\"added\":true,\"server_only\":42}", "\"v3\"");
            record.SaveAsync().GetAwaiter().GetResult();
            var patch = fake.Requests.Last().JsonBody;
            Assert.That(patch, Does.Contain("\"profile\":{\"region\":\"us\"}"));
            Assert.That(patch, Does.Contain("\"tags\":[\"one\",\"two\"]"));
            Assert.That(patch, Does.Contain("\"remove_me\":null"));
            Assert.That(patch, Does.Contain("\"nullable\":null"));
            Assert.That(patch, Does.Not.Contain("title"));
            Assert.That(patch, Does.Not.Contain("server_only"));
            Assert.That(record.HasPendingChanges, Is.False);
        }

        [UnityTest]
        public IEnumerator Save_queued_call_sends_replacement_value_and_reverted_fields_with_new_etag()
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueJson(200, RecordJson("rec_1", "sword", "Old"), "\"v1\"");
            var record = CreateSet<InventoryItem>(fake, "ent_inventory").LoadAsync("rec_1").GetAwaiter().GetResult();
            var response = new TaskCompletionSource<PlayServRuntimeDataResponse>();
            fake.DataHandler = (_, __) => response.Task;
            record.Value.DisplayName = "Sent";
            var first = record.SaveAsync();
            record.Value = new InventoryItem { Code = "shield", DisplayName = "Old" };
            var second = record.SaveAsync();
            Assert.That(fake.Requests, Has.Count.EqualTo(2), "The second save must wait for the first.");
            fake.DataHandler = null;
            fake.EnqueueJson(200, RecordJson("rec_1", "shield", "Old"), "\"v3\"");
            response.SetResult(new PlayServRuntimeDataResponse(200, RecordJson("rec_1", "sword", "Sent"), "\"v2\"", null));
            yield return WaitForSaveTask(Task.WhenAll(first, second));
            first.GetAwaiter().GetResult();
            second.GetAwaiter().GetResult();
            Assert.That(fake.Requests, Has.Count.EqualTo(3));
            Assert.That(fake.Requests.Last().IfMatch, Is.EqualTo("\"v2\""));
            Assert.That(fake.Requests.Last().JsonBody, Does.Contain("\"code_wire\":\"shield\""));
            Assert.That(fake.Requests.Last().JsonBody, Does.Contain("\"display_name\":\"Old\""));
            Assert.That(record.HasPendingChanges, Is.False);
        }

        [UnityTest]
        public IEnumerator Save_failure_or_cancellation_keeps_local_value_snapshot_and_etag()
        {
            foreach (var failure in new Exception[]
            {
                HttpError(412, "precondition_failed", "{\"code\":\"precondition_failed\"}"),
                new TimeoutException("Simulated timeout"), new OperationCanceledException()
            })
            {
                var fake = new FakeDataHttpClient();
                fake.EnqueueJson(200, RecordJson("rec_1", "sword", "Old"), "\"v1\"");
                var record = CreateSet<InventoryItem>(fake, "ent_inventory").LoadAsync("rec_1").GetAwaiter().GetResult();
                var snapshot = record.CanonicalSnapshot;
                var response = new TaskCompletionSource<PlayServRuntimeDataResponse>();
                fake.DataHandler = (_, __) => response.Task;
                record.Value.DisplayName = "Sent";
                var save = record.SaveAsync();
                record.Value.DisplayName = "New local edit";
                response.SetException(failure);
                yield return WaitForSaveTask(save);
                Assert.That(save.IsFaulted || save.IsCanceled, Is.True);
                if (failure is OperationCanceledException)
                    Assert.Catch<OperationCanceledException>(() => save.GetAwaiter().GetResult());
                else
                    Assert.Catch<PlayServDataException>(() => save.GetAwaiter().GetResult());
                Assert.That(record.Value.DisplayName, Is.EqualTo("New local edit"));
                Assert.That(record.CanonicalSnapshot, Is.EqualTo(snapshot));
                Assert.That(record.ETag, Is.EqualTo("\"v1\""));
                Assert.That(record.HasPendingChanges, Is.True);
                Assert.That(fake.Requests, Has.Count.EqualTo(2));
            }
        }

        [UnityTest]
        public IEnumerator Save_does_not_keep_a_change_that_already_matches_server_response()
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueJson(200, RecordJson("rec_1", "sword", "Old"), "\"v1\"");
            var record = CreateSet<InventoryItem>(fake, "ent_inventory").LoadAsync("rec_1").GetAwaiter().GetResult();
            var response = new TaskCompletionSource<PlayServRuntimeDataResponse>();
            fake.DataHandler = (_, __) => response.Task;
            record.Value.DisplayName = "Sent";
            var save = record.SaveAsync();
            record.Value.DisplayName = "Canonical";
            response.SetResult(new PlayServRuntimeDataResponse(200, RecordJson("rec_1", "sword", "Canonical"), "\"v2\"", null));
            yield return WaitForSaveTask(save);
            save.GetAwaiter().GetResult();
            Assert.That(record.HasPendingChanges, Is.False);
            record.SaveAsync().GetAwaiter().GetResult();
            Assert.That(fake.Requests, Has.Count.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator Save_singleton_preserves_inflight_edits_for_next_save()
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueJson(200, SingletonJson("Old"), "\"v1\"");
            var singleton = CreateSet<GameConfig>(fake, "ent_config").GetSingletonAsync().GetAwaiter().GetResult();
            var response = new TaskCompletionSource<PlayServRuntimeDataResponse>();
            fake.DataHandler = (_, __) => response.Task;
            singleton.Value.Message = "Sent";
            var save = singleton.SaveAsync();
            singleton.Value.Message = "New edit";
            response.SetResult(new PlayServRuntimeDataResponse(200, SingletonJson("Sent"), "\"v2\"", null));
            yield return WaitForSaveTask(save);
            save.GetAwaiter().GetResult();
            Assert.That(singleton.Value.Message, Is.EqualTo("New edit"));
            Assert.That(singleton.ETag, Is.EqualTo("\"v2\""));
            Assert.That(singleton.HasPendingChanges, Is.True);
            fake.DataHandler = null;
            fake.EnqueueJson(200, SingletonJson("New edit"), "\"v3\"");
            singleton.SaveAsync().GetAwaiter().GetResult();
            Assert.That(fake.Requests.Last().JsonBody, Is.EqualTo("{\"message\":\"New edit\"}"));
            Assert.That(fake.Requests.Last().IfMatch, Is.EqualTo("\"v2\""));
            Assert.That(singleton.HasPendingChanges, Is.False);
        }

        private static IEnumerator WaitForSaveTask(Task task)
        {
            var deadline = Time.realtimeSinceStartup + 5;
            while (!task.IsCompleted && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(task.IsCompleted, Is.True, "A save or queued operation did not finish.");
        }
    }
}
