using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.Data;
using Playserv.DataSubscription;
using Playserv.Serialization;
using Playserv.Http.Interfaces;
using Newtonsoft.Json.Linq;
using UnityEngine.TestTools;

namespace Playserv.Tests.Runtime
{
    public sealed partial class PlayServTypedRecordsTests
    {
        [UnityTest]
        public IEnumerator ManagedFields_reload_installs_state_before_fields_whole_and_completion() => QueueRun(async () =>
        {
            var server = new ManagedServer();
            var record = await server.Set.LoadAsync("rec_1");
            var order = new List<string>(); var tags = new List<string>();
            PlayServFieldChange<InventoryItem> observed = null;
            using var watch = WatchRecord(record, "code_wire", change =>
            { observed = change; tags.Add(record.ETag); tags.Add(record.Value.DisplayName); order.Add("field"); });
            OnRecordChanged(record, _ => order.Add("whole"));
            Assert.That(order, Is.Empty);
            record.Value.Code = "unsaved";
            server.Change("remote", "Remote");
            await record.ReloadAsync(); order.Add("complete");
            Assert.That(order, Is.EqualTo(new[] { "field", "whole", "complete" }));
            Assert.That(tags, Is.EqualTo(new[] { server.Tag, "Remote" }));
            Assert.That(observed.Previous, Is.EqualTo("unsaved"));
            Assert.That(observed.Current, Is.EqualTo("remote"));
            Assert.That(observed.Value.DisplayName, Is.EqualTo("Remote"));
            Assert.That(observed.IsInitial, Is.False);
            Assert.That(record.HasPendingChanges, Is.False);
        });

        [UnityTest]
        public IEnumerator ManagedFields_collection_push_notifies_field_before_record_events_once() => QueueRun(async () =>
        {
            var server = new ManagedServer();
            using var adapter = new PlayServDataSubscriptionAdapter(server.Bus, new FakeLogger(), new NewtonsoftJsonCodec());
            using var live = await OpenManaged(adapter, server.Set);
            var record = live.Items.Single(); var value = record.Value;
            var order = new List<string>();
            using var watch = WatchRecord(record, "code_wire", _ => order.Add("field"));
            OnRecordChanged(record, _ => order.Add("record"));
            live.RecordChanged += _ => order.Add("collection");
            server.Change("remote", "Remote");
            await live.RefreshAsync(); order.Add("complete");
            Assert.That(order, Is.EqualTo(new[] { "field", "record", "collection", "complete" }));
            Assert.That(record.Value, Is.SameAs(value));
        });

        [UnityTest]
        public IEnumerator ManagedFields_record_subscription_push_notifies_field_before_subscription() => QueueRun(async () =>
        {
            var http = new FakeDataHttpClient();
            http.EnqueueJson(200, RecordJson("rec_1", "old", "Old"), "\"v1\"");
            var bus = new RecordSubscriptionCommandBus { InitialRecordData = RecordFields("old", "Old") };
            using var adapter = new PlayServDataSubscriptionAdapter(bus, new FakeLogger(), new NewtonsoftJsonCodec());
            var set = CreateSet<InventoryItem>(http, "ent_inventory", subscribeRecordAsync: adapter.SelectTypedRecordAsync);
            var record = await set.LoadAsync("rec_1");
            using var live = await record.SubscribeAsync();
            var order = new List<string>();
            using var watch = WatchRecord(record, "code_wire", _ => order.Add("field"));
            OnRecordChanged(record, _ => order.Add("record"));
            live.Changed += _ => order.Add("subscription");
            http.EnqueueJson(200, RecordJson("rec_1", "remote", "Remote", "2026-08-18T00:00:00Z"), "\"v2\"");
            bus.EmitRecordUpdate(RecordFields("remote", "Remote"));
            await QueueUntil(() => order.Contains("subscription"));
            Assert.That(order, Is.EqualTo(new[] { "field", "record", "subscription" }));
        });

        [UnityTest]
        public IEnumerator ManagedFields_reference_rehydration_keeps_watched_handle() => QueueRun(async () =>
        {
            var http = ReferenceHttp();
            var client = new PlayServRecordsClient(Settings(), http, new NewtonsoftJsonCodec());
            var reference = new PlayServRecordSet<RefChild>(client, "ent_children").Reference("rec_child");
            http.EnqueueJson(200, ChildJson, "\"v1\"");
            var record = await reference.LoadAsync();
            http.EnqueueJson(200, ChildJson.Replace("Canonical", "Refreshed"), "\"v2\"");
            var reloaded = await reference.ReloadAsync();
            Assert.That(reloaded, Is.SameAs(record), "Rehydration must keep the canonical handle that owns observers.");
        });

        [UnityTest]
        public IEnumerator ManagedFields_reference_reload_delivers_before_completion() => QueueRun(async () =>
        {
            var http = ReferenceHttp();
            var reference = new PlayServRecordSet<RefChild>(new PlayServRecordsClient(Settings(), http, new NewtonsoftJsonCodec()), "ent_children").Reference("rec_child");
            http.EnqueueJson(200, ChildJson, "\"v1\"");
            var record = await reference.LoadAsync();
            var order = new List<string>(); PlayServFieldChange<RefChild> observed = null;
            using var watch = WatchRecord(record, "name", change => { observed = change; order.Add("field"); });
            OnRecordChanged(record, _ => order.Add("whole"));
            http.EnqueueJson(200, ChildJson.Replace("Canonical", "Refreshed"), "\"v2\"");
            await reference.ReloadAsync(); order.Add("complete");
            Assert.That(order, Is.EqualTo(new[] { "field", "whole", "complete" }));
            Assert.That(observed.Previous, Is.EqualTo("Canonical"));
        });

        [UnityTest]
        public IEnumerator ManagedFields_clean_refresh_does_not_claim_unsaved_overwrite() => QueueRun(async () =>
        {
            var server = new ManagedServer(); var record = await server.Set.LoadAsync("rec_1");
            PlayServRecordChange<InventoryItem> change = null;
            record.Changed += value => change = value;
            server.Change("remote", "Remote"); await record.ReloadAsync();
            Assert.That(change.OverwrotePendingChanges, Is.False);
        });

        [UnityTest]
        public IEnumerator ManagedFields_explicit_reload_reaches_collection_before_completion() => QueueRun(async () =>
        {
            var server = new ManagedServer();
            using var adapter = new PlayServDataSubscriptionAdapter(server.Bus, new FakeLogger(), new NewtonsoftJsonCodec());
            using var live = await OpenManaged(adapter, server.Set);
            var record = live.Items.Single(); var order = new List<string>();
            using var watch = record.ObserveField("code_wire", _ => order.Add("field"));
            live.RecordChanged += _ => order.Add("collection");
            server.Change("remote", "Remote"); await record.ReloadAsync(); order.Add("complete");
            Assert.That(order, Is.EqualTo(new[] { "field", "collection", "complete" }));
            live.Dispose(); server.Change("later", "Later"); await record.ReloadAsync();
            Assert.That(order.Count(item => item == "collection"), Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator ManagedFields_explicit_reload_reaches_subscription_before_completion() => QueueRun(async () =>
        {
            var http = new FakeDataHttpClient();
            http.EnqueueJson(200, RecordJson("rec_1", "old", "Old"), "\"v1\"");
            var bus = new RecordSubscriptionCommandBus { InitialRecordData = RecordFields("old", "Old") };
            using var adapter = new PlayServDataSubscriptionAdapter(bus, new FakeLogger(), new NewtonsoftJsonCodec());
            var record = await CreateSet<InventoryItem>(http, "ent_inventory", subscribeRecordAsync: adapter.SelectTypedRecordAsync).LoadAsync("rec_1");
            using var live = await record.SubscribeAsync();
            var order = new List<string>();
            using var watch = record.ObserveField("code_wire", _ => order.Add("field"));
            live.Changed += _ => order.Add("subscription");
            http.EnqueueJson(200, RecordJson("rec_1", "remote", "Remote", "2026-08-18T00:00:00Z"), "\"v2\"");
            await record.ReloadAsync(); order.Add("complete");
            Assert.That(order, Is.EqualTo(new[] { "field", "subscription", "complete" }));
        });

        [UnityTest]
        public IEnumerator ManagedFields_conflict_precedes_whole_record_but_follows_fields() => QueueRun(async () =>
        {
            var server = new ManagedServer();
            using var adapter = new PlayServDataSubscriptionAdapter(server.Bus, new FakeLogger(), new NewtonsoftJsonCodec());
            using var live = await OpenManaged(adapter, server.Set); var record = live.Items.Single();
            var order = new List<string>();
            using var watch = record.ObserveField("code_wire", _ => order.Add("field"));
            record.Changed += _ => order.Add("record"); live.Conflict += _ => order.Add("conflict");
            live.RecordChanged += _ => order.Add("collection");
            record.Value.Code = "unsaved"; server.Change("remote", "Remote"); await live.RefreshAsync();
            Assert.That(order, Is.EqualTo(new[] { "field", "conflict", "record", "collection" }));
        });

        [UnityTest]
        public IEnumerator ManagedFields_reference_watch_survives_failed_rehydration() => QueueRun(async () =>
        {
            var http = ReferenceHttp();
            var reference = new PlayServRecordSet<RefChild>(new PlayServRecordsClient(Settings(), http, new NewtonsoftJsonCodec()), "ent_children").Reference("rec_child");
            http.EnqueueJson(200, ChildJson, "\"v1\""); var record = await reference.LoadAsync();
            var changes = 0; using var watch = record.ObserveField("name", _ => changes++);
            http.EnqueueException(new PlayServRuntimeHttpException("offline", 0, "{}", "network", true));
            await CaptureExceptionAsync<PlayServDataException>(() => reference.ReloadAsync());
            Assert.That(reference.State, Is.EqualTo(PlayServRecordRefState.Faulted));
            Assert.That(changes, Is.Zero);
            http.EnqueueJson(200, ChildJson.Replace("Canonical", "Recovered"), "\"v2\"");
            Assert.That(await reference.ReloadAsync(), Is.SameAs(record));
            Assert.That(changes, Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator ManagedFields_registration_local_edits_saves_and_unchanged_reload_are_silent() => QueueRun(async () =>
        {
            var server = new ManagedServer(); var record = await server.Set.LoadAsync("rec_1");
            var fields = 0; var whole = 0;
            using var watch = record.ObserveField("code_wire", _ => fields++); record.Changed += _ => whole++;
            await record.SaveAsync(); await record.ReloadAsync(); record.Value.Code = "edited";
            await record.SaveAsync(); await record.ReloadAsync();
            Assert.That(fields, Is.Zero); Assert.That(whole, Is.Zero);
        });

        [UnityTest]
        public IEnumerator ManagedFields_nested_change_is_one_detached_top_level_notification() => QueueRun(async () =>
        {
            var server = new SplitServer(); var record = await server.Set.LoadAsync("rec_split");
            var changes = new List<PlayServFieldChange<SplitModel>>();
            using var watch = record.ObserveField("parts", changes.Add);
            server.Row["parts"] = JArray.Parse("[{\"Score\":42},{\"Score\":9}]");
            await record.ReloadAsync(); await record.ReloadAsync();
            Assert.That(changes, Has.Count.EqualTo(1));
            Assert.That(changes[0].Value.Parts[0].Score, Is.EqualTo(42));
            changes[0].Value.Parts[0].Score = 99;
            Assert.That(record.Value.Parts[0].Score, Is.EqualTo(42));
            Assert.That(changes[0].Value.Parts[0].Score, Is.EqualTo(42));
        });

        [UnityTest]
        public IEnumerator ManagedFields_reference_equality_uses_ID_and_payload_can_deserialize_references() => QueueRun(async () =>
        {
            var http = ReferenceHttp(); var set = new PlayServRecordSet<RefParent>(new PlayServRecordsClient(Settings(), http, new NewtonsoftJsonCodec()), "ent_parents");
            http.EnqueueJson(200, ParentJson("\"rec_child\""), "\"v1\""); var record = await set.LoadAsync("rec_parent");
            var changes = new List<PlayServFieldChange<RefParent>>(); using var watch = record.ObserveField("child", changes.Add);
            http.EnqueueJson(200, ParentJson("\"rec_child\""), "\"v2\""); await record.ReloadAsync();
            Assert.That(changes, Is.Empty);
            http.EnqueueJson(200, ParentJson("\"rec_other\""), "\"v3\""); await record.ReloadAsync();
            Assert.That(changes, Has.Count.EqualTo(1)); Assert.That(changes[0].Previous, Is.EqualTo("rec_child"));
            Assert.That(changes[0].Value.Child.Id, Is.EqualTo("rec_other"));
        });

        [UnityTest]
        public IEnumerator ManagedFields_disposal_exceptions_and_reentrant_reload_preserve_delivery() => QueueRun(async () =>
        {
            var server = new ManagedServer(); var record = await server.Set.LoadAsync("rec_1");
            var calls = 0; var removedCalls = 0; IDisposable removed = null; Task queued = null;
            using var first = record.ObserveField("code_wire", _ =>
            { removed.Dispose(); queued = record.ReloadAsync(); throw new InvalidOperationException("callback failure"); });
            using var good = record.ObserveField("code_wire", _ => calls++);
            removed = record.ObserveField("code_wire", _ => removedCalls++);
            server.Change("remote", "Remote"); await record.ReloadAsync(); await queued;
            Assert.That(calls, Is.EqualTo(1)); Assert.That(removedCalls, Is.Zero);
            first.Dispose(); good.Dispose(); server.Change("later", "Later"); await record.ReloadAsync();
            Assert.That(calls, Is.EqualTo(1));
        });

        private static IDisposable WatchRecord<T>(PlayServRecord<T> record, string field, Action<PlayServFieldChange<T>> callback)
        {
            return PlayServFieldObservationExtensions.ObserveField(record, field, callback);
        }

        private static void OnRecordChanged<T>(PlayServRecord<T> record, Action<PlayServRecordChange<T>> callback)
        {
            record.Changed += callback;
        }

        [UnityTest]
        public IEnumerator ManagedFields_reference_reload_after_delete_preserves_missing_semantics() => QueueRun(async () =>
        {
            var http = ReferenceHttp();
            var reference = new PlayServRecordSet<RefChild>(new PlayServRecordsClient(Settings(), http, new NewtonsoftJsonCodec()), "ent_children").Reference("rec_child");
            http.EnqueueJson(200, ChildJson, "\"v1\""); var record = await reference.LoadAsync();
            var calls = 0; using var watch = record.ObserveField("name", _ => calls++);
            http.EnqueueJson(204, "{}"); await record.DeleteAsync();
            http.EnqueueException(new PlayServRuntimeHttpException("missing", 404, "{}", "record_not_found", false));
            Assert.That(await reference.ReloadAsync(), Is.Null);
            Assert.That(reference.State, Is.EqualTo(PlayServRecordRefState.Missing));
            Assert.That(calls, Is.Zero);
        });

        [UnityTest]
        public IEnumerator ManagedFields_explicit_reload_ignores_older_state() => QueueRun(async () =>
        {
            var server = new ManagedServer(); server.Change("newest", "Newest");
            var record = await server.Set.LoadAsync("rec_1"); var tag = record.ETag;
            var calls = 0; using var watch = record.ObserveField("code_wire", _ => calls++);
            record.Changed += _ => calls++;
            server.Http.DataHandler = (_, ct) => Task.FromResult(new PlayServRuntimeDataResponse(200,
                RecordJson("rec_1", "stale", "Stale", "2026-08-16T00:00:00Z"), "\"old\"", null));
            await record.ReloadAsync();
            Assert.That(record.Value.Code, Is.EqualTo("newest")); Assert.That(record.ETag, Is.EqualTo(tag));
            Assert.That(calls, Is.Zero);
        });

        [UnityTest]
        public IEnumerator ManagedFields_reference_reload_ignores_older_state() => QueueRun(async () =>
        {
            var http = ReferenceHttp();
            var reference = new PlayServRecordSet<RefChild>(new PlayServRecordsClient(Settings(), http, new NewtonsoftJsonCodec()), "ent_children").Reference("rec_child");
            http.EnqueueJson(200, ChildJson, "\"v2\""); var record = await reference.LoadAsync();
            var calls = 0; using var watch = record.ObserveField("name", _ => calls++);
            http.EnqueueJson(200, ChildJson.Replace("Canonical", "Stale").Replace("2026-09-18", "2026-09-17"), "\"v1\"");
            await reference.ReloadAsync();
            Assert.That(record.Value.Name, Is.EqualTo("Canonical")); Assert.That(calls, Is.Zero);
            Assert.That(reference.State, Is.EqualTo(PlayServRecordRefState.Loaded));
        });

        [UnityTest]
        public IEnumerator ManagedFields_context_change_in_callback_suppresses_remaining_delivery() => QueueRun(async () =>
        {
            var http = ReferenceHttp(); var settings = Settings(); settings.PlayerId = "first";
            var reference = new PlayServRecordSet<RefChild>(new PlayServRecordsClient(settings, http, new NewtonsoftJsonCodec()), "ent_children").Reference("rec_child");
            http.EnqueueJson(200, ChildJson, "\"v1\""); var record = await reference.LoadAsync();
            var first = 0; var later = 0;
            using var changer = record.ObserveField("name", _ => { first++; settings.PlayerId = "second"; });
            using var stopped = record.ObserveField("name", _ => later++); record.Changed += _ => later++;
            http.EnqueueJson(200, ChildJson.Replace("Canonical", "Remote"), "\"v2\"");
            await CaptureExceptionAsync<InvalidOperationException>(() => reference.ReloadAsync());
            Assert.That(first, Is.EqualTo(1)); Assert.That(later, Is.Zero);
        });

        [UnityTest]
        public IEnumerator ManagedFields_cancelled_wait_does_not_cancel_other_waiters_or_notifications() => QueueRun(async () =>
        {
            var server = new ManagedServer(); var record = await server.Set.LoadAsync("rec_1");
            var gate = new TaskCompletionSource<PlayServRuntimeDataResponse>(); var started = false; var calls = 0;
            using var watch = record.ObserveField("code_wire", _ => calls++);
            server.Change("remote", "Remote");
            server.Http.DataHandler = (_, ct) => { started = true; return gate.Task; };
            using var cancellation = new CancellationTokenSource();
            var abandoned = record.ReloadAsync(cancellation.Token); await QueueUntil(() => started);
            var kept = record.ReloadAsync(); cancellation.Cancel();
            await CaptureExceptionAsync<OperationCanceledException>(() => abandoned);
            gate.SetResult(new PlayServRuntimeDataResponse(200, server.Row.ToString(), server.Tag, null));
            await kept; Assert.That(calls, Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator ManagedFields_subscription_initial_synchronization_is_silent() => QueueRun(async () =>
        {
            var http = new FakeDataHttpClient();
            http.EnqueueJson(200, RecordJson("rec_1", "old", "Old"), "\"v1\"");
            var bus = new RecordSubscriptionCommandBus { InitialRecordData = RecordFields("remote", "Remote") };
            using var adapter = new PlayServDataSubscriptionAdapter(bus, new FakeLogger(), new NewtonsoftJsonCodec());
            var record = await CreateSet<InventoryItem>(http, "ent_inventory", subscribeRecordAsync: adapter.SelectTypedRecordAsync).LoadAsync("rec_1");
            var calls = 0; using var watch = record.ObserveField("code_wire", _ => calls++); record.Changed += _ => calls++;
            http.EnqueueJson(200, RecordJson("rec_1", "remote", "Remote", "2026-08-18T00:00:00Z"), "\"v2\"");
            using var live = await record.SubscribeAsync();
            Assert.That(record.Value.Code, Is.EqualTo("remote")); Assert.That(calls, Is.Zero);
        });

        private sealed class ObservedPayload { [PlayServJsonName("payload")] public Dictionary<string, object> Payload; }
        [UnityTest]
        public IEnumerator ManagedFields_object_order_is_equal_and_null_is_one_change() => QueueRun(async () =>
        {
            var http = new FakeDataHttpClient(); http.ConfigureDefaultCatalogue("ent_payload", nameof(ObservedPayload), false);
            var set = CreateSet<ObservedPayload>(http, "ent_payload");
            http.EnqueueJson(200, "{\"id\":\"r\",\"payload\":{\"a\":1,\"b\":[2,3]}}", "\"v1\""); var record = await set.LoadAsync("r");
            var changes = new List<PlayServFieldChange<ObservedPayload>>(); using var watch = record.ObserveField("payload", changes.Add);
            http.EnqueueJson(200, "{\"id\":\"r\",\"payload\":{\"b\":[2,3],\"a\":1}}", "\"v2\""); await record.ReloadAsync();
            Assert.That(changes, Is.Empty);
            http.EnqueueJson(200, "{\"id\":\"r\",\"payload\":null}", "\"v3\""); await record.ReloadAsync();
            Assert.That(changes, Has.Count.EqualTo(1)); Assert.That(changes[0].Current, Is.Null);
            Assert.That(changes[0].Previous, Is.Not.Null);
        });
    }
}
