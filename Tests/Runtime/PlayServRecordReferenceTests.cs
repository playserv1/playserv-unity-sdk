using System;
using System.Collections;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;
using Playserv.Data;
using Playserv.Http.Interfaces;
using Playserv.Serialization;
using Playserv.Wrapper;

namespace Playserv.Tests.Runtime
{
    public sealed partial class PlayServTypedRecordsTests
    {
        [Test]
        public void References_read_ID_null_arrays_and_write_only_IDs()
        {
            var fake = ReferenceHttp(); var client = new PlayServRecordsClient(Settings(), fake, new NewtonsoftJsonCodec());
            fake.EnqueueJson(200, ParentJson("\"rec_child\""), "\"parent-1\"");
            var parents = new PlayServRecordSet<RefParent>(client, "ent_parents");
            var parent = parents.LoadAsync("rec_parent").GetAwaiter().GetResult();
            Assert.That(parent.Value.Child.Id, Is.EqualTo("rec_child"));
            Assert.That(parent.Value.Optional, Is.Null);
            Assert.That(parent.Value.Children.Select(r => r.Id), Is.EqualTo(new[] { "rec_child", "rec_other" }));
            Assert.That(parent.Value.Child.State, Is.EqualTo(PlayServRecordRefState.Unloaded));
            fake.EnqueueJson(201, ParentJson("\"rec_child\""), "\"parent-2\"");
            parents.CreateAsync(parent.Value).GetAwaiter().GetResult();
            var body = new NewtonsoftJsonCodec().Deserialize<System.Collections.Generic.Dictionary<string,object>>(fake.Requests.Last().JsonBody);
            Assert.That(body["child"], Is.EqualTo("rec_child"));
            Assert.That(fake.Requests.Last().JsonBody, Does.Not.Contain("State").And.Not.Contain("Value"));
        }
        [Test]
        public void Expanded_reference_requires_canonical_load_and_hydration_never_patches_parent()
        {
            var fake = ReferenceHttp(); var client = new PlayServRecordsClient(Settings(), fake, new NewtonsoftJsonCodec());
            fake.EnqueueJson(200, ParentJson("{\"id\":\"rec_child\",\"name\":\"Preview\",\"updated_at\":\"2026-09-01T00:00:00Z\"}"), "\"parent-1\"");
            var parent = new PlayServRecordSet<RefParent>(client, "ent_parents").LoadAsync("rec_parent").GetAwaiter().GetResult();
            Assert.That(parent.Value.Child.Value.Name, Is.EqualTo("Preview"));
            Assert.That(parent.Value.Child.Record, Is.Null);
            Assert.That(parent.Value.Child.State, Is.EqualTo(PlayServRecordRefState.Expanded));
            Assert.That(parent.HasPendingChanges, Is.False);
            fake.EnqueueJson(200, ChildJson, "\"child-canonical\"");
            var child = parent.Value.Child.LoadAsync().GetAwaiter().GetResult();
            Assert.That(child.ETag, Is.EqualTo("\"child-canonical\""));
            Assert.That(child.Value.Name, Is.EqualTo("Canonical"));
            child.Value.Name = "Child edit";
            Assert.That(parent.HasPendingChanges, Is.False);
            var count = fake.Requests.Count; parent.SaveAsync().GetAwaiter().GetResult();
            Assert.That(fake.Requests.Count, Is.EqualTo(count));
        }
        [UnityTest]
        public IEnumerator Reference_concurrent_loads_share_IO_and_one_cancel_does_not_cancel_another() => RunRef(async () =>
        {
            var fake = ReferenceHttp(); var client = new PlayServRecordsClient(Settings(), fake, new NewtonsoftJsonCodec());
            var pending = new TaskCompletionSource<PlayServRuntimeDataResponse>();
            fake.DataHandler = (request, ct) => pending.Task;
            var reference = new PlayServRecordSet<RefChild>(client, "ent_children").Reference("rec_child");
            using var cancel = new CancellationTokenSource();
            var first = reference.LoadAsync(cancel.Token); var second = reference.LoadAsync();
            Assert.That(reference.State, Is.EqualTo(PlayServRecordRefState.Loading));
            Assert.That(fake.Requests.Count, Is.EqualTo(1));
            cancel.Cancel();
            try { await first; Assert.Fail("Cancellation expected"); } catch (OperationCanceledException) { }
            pending.SetResult(new PlayServRuntimeDataResponse(200, ChildJson, "\"c1\"", null));
            Assert.That((await second).Value.Name, Is.EqualTo("Canonical"));
            Assert.That(await reference.LoadAsync(), Is.SameAs(await second));
        });
        [TestCase(404, PlayServRecordRefState.Missing)]
        [TestCase(403, PlayServRecordRefState.Faulted)]
        [TestCase(0, PlayServRecordRefState.Faulted)]
        public void Reference_404_is_missing_while_ACL_and_network_remain_errors(int status, PlayServRecordRefState state)
        {
            var fake = ReferenceHttp(); var reference = new PlayServRecordSet<RefChild>(new PlayServRecordsClient(Settings(), fake, new NewtonsoftJsonCodec()), "ent_children").Reference("rec_child");
            fake.EnqueueException(new PlayServRuntimeHttpException("fixture", status, "{}", status == 404 ? "record_not_found" : "denied", status == 0));
            if (status == 404) Assert.That(reference.LoadAsync().GetAwaiter().GetResult(), Is.Null);
            else Assert.Catch(() => reference.LoadAsync().GetAwaiter().GetResult());
            Assert.That(reference.State, Is.EqualTo(state));
            Assert.That(reference.Error == null, Is.EqualTo(status == 404));
        }
        [Test]
        public void Reference_batch_preserves_duplicates_and_uses_existing_ID_query()
        {
            var fake = ReferenceHttp(); var set = new PlayServRecordSet<RefChild>(new PlayServRecordsClient(Settings(), fake, new NewtonsoftJsonCodec()), "ent_children");
            fake.EnqueueJson(200, "{\"data\":[" + ChildJson + "],\"page\":{\"has_more\":false,\"cursor_next\":null}}");
            var result = set.LoadReferencesAsync(new[] { "rec_child", "rec_child", "rec_missing" }).GetAwaiter().GetResult();
            Assert.That(fake.Requests.Count, Is.EqualTo(1));
            Assert.That(result.Items.Count, Is.EqualTo(3));
            Assert.That(result.Items[0].Value.Value.Name, Is.EqualTo("Canonical"));
            Assert.That(result.Items[1].Value.Record, Is.Not.SameAs(result.Items[0].Value.Record));
            Assert.That(result.Items[2].Value.State, Is.EqualTo(PlayServRecordRefState.Missing));
        }
        [UnityTest]
        public IEnumerator Reference_rejects_changed_context_and_discards_inflight_old_response() => RunRef(async () =>
        {
            var fake = ReferenceHttp(); var settings = Settings(); settings.PlayerId = "first";
            var client = new PlayServRecordsClient(settings, fake, new NewtonsoftJsonCodec());
            var reference = new PlayServRecordSet<RefChild>(client, "ent_children").Reference("rec_child");
            var pending = new TaskCompletionSource<PlayServRuntimeDataResponse>(); fake.DataHandler = (request, ct) => pending.Task;
            var load = reference.LoadAsync(); settings.PlayerId = "second";
            pending.SetResult(new PlayServRuntimeDataResponse(200, ChildJson, null, null));
            await CaptureExceptionAsync<InvalidOperationException>(async () => await load);
            var count = fake.Requests.Count;
            await CaptureExceptionAsync<InvalidOperationException>(async () => await reference.ReloadAsync());
            Assert.That(fake.Requests.Count, Is.EqualTo(count));
        });
        [Test]
        public void Reference_reload_refreshes_missing_and_precancel_does_no_IO()
        {
            var fake = ReferenceHttp();
            var reference = new PlayServRecordSet<RefChild>(new PlayServRecordsClient(Settings(), fake, new NewtonsoftJsonCodec()), "ent_children").Reference("rec_child");
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            Assert.Throws<OperationCanceledException>(() => reference.LoadAsync(cancelled.Token));
            Assert.That(fake.AllRequests.Count, Is.Zero);
            fake.EnqueueException(new PlayServRuntimeHttpException("missing", 404, "{}", "record_not_found", false));
            Assert.That(reference.LoadAsync().GetAwaiter().GetResult(), Is.Null);
            Assert.That(reference.LoadAsync().GetAwaiter().GetResult(), Is.Null);
            Assert.That(fake.Requests.Count, Is.EqualTo(1));
            fake.EnqueueJson(200, ChildJson, "\"fresh\"");
            Assert.That(reference.ReloadAsync().GetAwaiter().GetResult().ETag, Is.EqualTo("\"fresh\""));
        }

        [UnityTest]
        public IEnumerator Reference_context_change_during_token_await_never_sends_request() => RunRef(async () =>
        {
            var fake = ReferenceHttp(); var settings = Settings();
            var token = new TaskCompletionSource<string>();
            settings.RuntimeTokenProvider = new Playserv.Runtime.Abstractions.PlayServDelegateRuntimeTokenProvider(ct => token.Task);
            var reference = new PlayServRecordSet<RefChild>(new PlayServRecordsClient(settings, fake, new NewtonsoftJsonCodec()), "ent_children").Reference("rec_child");
            var pending = reference.LoadAsync();
            settings.ClientToken = "pk_another_environment";
            token.SetResult("new-token");
            await CaptureExceptionAsync<InvalidOperationException>(async () => await pending);
            Assert.That(fake.AllRequests.Count, Is.Zero);
        });

        [Test]
        public void Reference_server_context_invalidation_and_original_acting_scope_are_preserved()
        {
            var first = ReferenceHttp(); var second = ReferenceHttp(); var current = true;
            var firstSet = new PlayServRecordSet<RefChild>(new PlayServRecordsClient(Settings(), first, new NewtonsoftJsonCodec(),
                requiresClientToken: false, accessSubject: PlayServDataAccessSubject.Server,
                isReferenceContextCurrent: () => current), "ent_children");
            var secondSet = new PlayServRecordSet<RefChild>(new PlayServRecordsClient(Settings(), second, new NewtonsoftJsonCodec(),
                requiresClientToken: false, accessSubject: PlayServDataAccessSubject.Server), "ent_children");
            var reference = firstSet.Reference("rec_child");
            secondSet.Reference("rec_child");
            first.EnqueueJson(200, ChildJson, "\"first\"");
            Assert.That(reference.LoadAsync().GetAwaiter().GetResult().ETag, Is.EqualTo("\"first\""));
            Assert.That(second.AllRequests.Count, Is.Zero);
            current = false;
            Assert.Throws<InvalidOperationException>(() => reference.LoadAsync());
            Assert.Throws<InvalidOperationException>(() => { var value = reference.Value; });
        }

        [Test]
        public void Reference_expanded_arrays_are_clean_and_parent_write_never_cascades()
        {
            var fake = ReferenceHttp(); var client = new PlayServRecordsClient(Settings(), fake, new NewtonsoftJsonCodec());
            var response = ParentJson("null").Replace("[\"rec_child\",\"rec_other\"]", "[" + ChildJson + "]");
            fake.EnqueueJson(200, response, "\"p1\"");
            var parent = new PlayServRecordSet<RefParent>(client, "ent_parents").LoadAsync("rec_parent").GetAwaiter().GetResult();
            Assert.That(parent.Value.Children[0].Value.Name, Is.EqualTo("Canonical"));
            Assert.That(parent.HasPendingChanges, Is.False);
            parent.Value.Children[0].Value.Name = "preview edit";
            parent.Value.Child = new PlayServRecordRef<RefChild>("rec_new");
            fake.EnqueueJson(200, ParentJson("\"rec_new\""), "\"p2\"");
            parent.SaveAsync().GetAwaiter().GetResult();
            Assert.That(fake.Requests.Last().JsonBody, Is.EqualTo("{\"child\":\"rec_new\"}"));
            Assert.That(fake.Requests.Count, Is.EqualTo(2));
        }

        [Test]
        public void Nested_expanded_reference_does_not_overwrite_sibling_parent_baseline()
        {
            var fake = ReferenceHttp(); var client = new PlayServRecordsClient(Settings(), fake, new NewtonsoftJsonCodec());
            fake.EnqueueJson(200, "{\"id\":\"parent\",\"child\":{\"id\":\"child\",\"other\":{\"id\":\"nested\",\"name\":\"Nested\"}},\"other\":\"root\"}", "\"p1\"");
            var parent = new PlayServRecordSet<NestedParent>(client, "ent_parents").LoadAsync("parent").GetAwaiter().GetResult();
            Assert.That(parent.Value.Other.Id, Is.EqualTo("root"));
            Assert.That(parent.Value.Child.Value.Other.Id, Is.EqualTo("nested"));
            Assert.That(parent.HasPendingChanges, Is.False);
            parent.SaveAsync().GetAwaiter().GetResult();
            Assert.That(fake.Requests.Count, Is.EqualTo(1));
        }
        private sealed class NestedChild { [PlayServJsonName("other")] public PlayServRecordRef<RefChild> Other; }
        private sealed class NestedParent
        {
            [PlayServJsonName("child")] public PlayServRecordRef<NestedChild> Child;
            [PlayServJsonName("other")] public PlayServRecordRef<RefChild> Other;
        }

        private static IEnumerator RunRef(Func<Task> check)
        { var task = check(); while (!task.IsCompleted) yield return null; task.GetAwaiter().GetResult(); }
        private static FakeDataHttpClient ReferenceHttp()
        {
            var fake = new FakeDataHttpClient();
            fake.EnqueueCatalogueJson("{\"data\":[{\"entity_id\":\"ent_parents\",\"name\":\"RefParent\",\"singleton\":false},{\"entity_id\":\"ent_children\",\"name\":\"RefChild\",\"singleton\":false}]}");
            return fake;
        }
        private const string ChildJson = "{\"id\":\"rec_child\",\"name\":\"Canonical\",\"created_at\":\"2026-09-01T00:00:00Z\",\"updated_at\":\"2026-09-18T00:00:00Z\"}";
        private static string ParentJson(string child) => "{\"id\":\"rec_parent\",\"child\":" + child + ",\"children\":[\"rec_child\",\"rec_other\"],\"optional\":null,\"created_at\":\"2026-09-01T00:00:00Z\",\"updated_at\":\"2026-09-18T00:00:00Z\"}";
        private sealed class RefChild { [PlayServJsonName("name")] public string Name; }
        private sealed class RefParent
        {
            [PlayServJsonName("child")] public PlayServRecordRef<RefChild> Child;
            [PlayServJsonName("children")] public PlayServRecordRef<RefChild>[] Children;
            [PlayServJsonName("optional")] public PlayServRecordRef<RefChild> Optional;
        }
    }
}
