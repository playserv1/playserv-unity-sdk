using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Playserv.Data;
using Playserv.DataSubscription;
using Playserv.DataSubscription.Requests;
using Playserv.DataSubscription.Responses;
using Playserv.Http.Interfaces;
using Playserv.Modules;
using Playserv.Proxy.Common;
using Playserv.Runtime.Abstractions;
using Playserv.Serialization;
using Playserv.Schema;
using Playserv.Wrapper;
using UnityEngine.TestTools;

namespace Playserv.Tests.Runtime
{
    public sealed partial class PlayServTypedRecordsTests
    {
        [UnityTest]
        public IEnumerator ManagedCollection_retains_handle_and_value_across_push_and_save() => QueueRun(async () =>
        {
            var server = new ManagedServer();
            using var adapter = new PlayServDataSubscriptionAdapter(server.Bus, new FakeLogger(), new NewtonsoftJsonCodec());
            using var subscription = (IDisposable)await OpenManaged(adapter, server.Set);
            var record = ManagedItems(subscription).Single();
            var value = record.Value;
            value.Ignored = "local UI state";
            server.Change("remote", null);
            server.Bus.Push();
            await QueueUntil(() => record.Value.Code == "remote");
            Assert.That(ManagedItems(subscription).Single(), Is.SameAs(record));
            Assert.That(record.Value, Is.SameAs(value));
            Assert.That(value.DisplayName, Is.Null);
            Assert.That(value.Ignored, Is.EqualTo("local UI state"));
            value.Code = "edited";
            var expectedTag = record.ETag;
            await record.SaveAsync();
            Assert.That(server.Http.Requests.Last().IfMatch, Is.EqualTo(expectedTag));
            Assert.That(record.Value, Is.SameAs(value));
            Assert.That(record.HasPendingChanges, Is.False);
        });

        [UnityTest]
        public IEnumerator ManagedCollection_stable_record_does_not_replace_value_on_reload() => QueueRun(async () =>
        {
            var server = new ManagedServer();
            var record = await server.Set.LoadAsync("rec_1");
            EnableStable(record);
            var value = record.Value;
            value.Ignored = "UI";
            server.Change("remote", null);
            await record.ReloadAsync();
            Assert.That(record.Value, Is.SameAs(value));
            Assert.That(value.Code, Is.EqualTo("remote"));
            Assert.That(value.DisplayName, Is.Null);
            Assert.That(value.Ignored, Is.EqualTo("UI"));
        });

        [UnityTest]
        public IEnumerator ManagedCollection_partial_split_save_keeps_pending_edits_in_stable_value() => QueueRun(async () =>
        {
            var server = new SplitServer { FailField = "parts" };
            server.Row["updated_at"] = "2026-08-17T00:00:00Z";
            var record = await server.Set.LoadAsync("rec_split");
            EnableStable(record);
            var value = record.Value;
            value.Name = "accepted"; value.Friend = "rec_friend";
            value.Parts = new[] { new SplitPart { Score = 9 } };
            await CaptureExceptionAsync<PlayServSplitSaveException<SplitModel>>(() => record.SaveAsync());
            Assert.That(record.Value, Is.SameAs(value));
            Assert.That(value.Parts.Single().Score, Is.EqualTo(9));
            Assert.That(record.HasPendingChanges, Is.True);
            server.FailField = null;
            await record.SaveAsync();
            Assert.That(record.Value, Is.SameAs(value));
            Assert.That(record.HasPendingChanges, Is.False);
        });

        private static void EnableStable<T>(PlayServRecord<T> record) =>
            typeof(PlayServRecord<T>).GetMethod("EnableStableValue", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(record, null);

        [Test]
        public void ManagedCollection_record_set_exposes_opt_in_managed_subscription()
        {
            Assert.That(typeof(PlayServRecordSet<InventoryItem>).GetMethod("SubscribeManagedAsync"), Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator ManagedCollection_filter_exit_detaches_without_deleting_and_reentry_gets_new_identity() => QueueRun(async () =>
        {
            var server = new ManagedServer();
            using var adapter = new PlayServDataSubscriptionAdapter(server.Bus, new FakeLogger(), new NewtonsoftJsonCodec());
            using var live = (IPlayServManagedCollection<InventoryItem>)await OpenManaged(adapter, server.Set);
            var retained = live.Items.Single();
            PlayServCollectionRemoval<InventoryItem> removal = null;
            live.Removed += change => removal = change;
            var rows = server.Bus.Rows;
            server.Bus.Rows = () => Array.Empty<object>(); server.Bus.Push();
            await QueueUntil(() => live.Items.Count == 0);
            Assert.That(removal.Reason, Is.EqualTo(PlayServCollectionRemovalReason.LeftCollection));
            Assert.That(retained.IsDeleted, Is.False);
            retained.Value.Code = "outside"; await retained.SaveAsync();
            server.Bus.Rows = rows; server.Bus.Push();
            await QueueUntil(() => live.Items.Count == 1);
            Assert.That(live.Items.Single(), Is.Not.SameAs(retained));
            Assert.That(live.Items.Single().Value, Is.Not.SameAs(retained.Value));
        });

        [UnityTest]
        public IEnumerator ManagedCollection_confirmed_delete_removes_member_and_stale_push_does_not_revive_it() => QueueRun(async () =>
        {
            var server = new ManagedServer();
            using var adapter = new PlayServDataSubscriptionAdapter(server.Bus, new FakeLogger(), new NewtonsoftJsonCodec());
            using var live = (IPlayServManagedCollection<InventoryItem>)await OpenManaged(adapter, server.Set);
            var record = live.Items.Single();
            PlayServCollectionRemoval<InventoryItem> removal = null;
            live.Removed += change => removal = change;
            await record.DeleteAsync();
            Assert.That(live.Items, Is.Empty);
            Assert.That(removal.Reason, Is.EqualTo(PlayServCollectionRemovalReason.Deleted));
            Assert.That(record.IsDeleted, Is.True);
            await live.RefreshAsync();
            Assert.That(live.Items, Is.Empty);
        });

        [UnityTest]
        public IEnumerator ManagedCollection_two_subscriptions_share_transport_but_not_mutable_objects() => QueueRun(async () =>
        {
            var server = new ManagedServer();
            using var adapter = new PlayServDataSubscriptionAdapter(server.Bus, new FakeLogger(), new NewtonsoftJsonCodec());
            var first = (IPlayServManagedCollection<InventoryItem>)await OpenManaged(adapter, server.Set);
            using var second = (IPlayServManagedCollection<InventoryItem>)await OpenManaged(adapter, server.Set);
            Assert.That(server.Bus.Opens, Is.EqualTo(1));
            var retained = first.Items.Single();
            Assert.That(second.Items.Single(), Is.Not.SameAs(retained));
            Assert.That(second.Items.Single().Value, Is.Not.SameAs(retained.Value));
            first.Dispose();
            Assert.That(server.Bus.Closes, Is.Zero);
            server.Change("remote", "Remote");
            await second.RefreshAsync();
            Assert.That(second.Items.Single().Value.Code, Is.EqualTo("remote"));
            Assert.That(retained.Value.Code, Is.EqualTo("old"));
            second.Dispose();
            Assert.That(server.Bus.Closes, Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator ManagedCollection_backend_wins_reports_local_snapshot_and_unchanged_refresh_keeps_edits() => QueueRun(async () =>
        {
            var server = new ManagedServer();
            using var adapter = new PlayServDataSubscriptionAdapter(server.Bus, new FakeLogger(), new NewtonsoftJsonCodec());
            using var live = (IPlayServManagedCollection<InventoryItem>)await OpenManaged(adapter, server.Set);
            var record = live.Items.Single(); var value = record.Value;
            PlayServRecordRealtimeConflict<InventoryItem> conflict = null;
            live.Conflict += error => conflict = error;
            value.Code = "unsaved";
            await live.RefreshAsync();
            Assert.That(value.Code, Is.EqualTo("unsaved"));
            Assert.That(conflict, Is.Null);
            server.Change("remote", "Remote");
            await live.RefreshAsync();
            Assert.That(conflict.LocalValue.Code, Is.EqualTo("unsaved"));
            Assert.That(conflict.LocalValue, Is.Not.SameAs(value));
            Assert.That(conflict.LocalChangedFields, Does.Contain("code_wire"));
            Assert.That(record.Value, Is.SameAs(value));
            Assert.That(value.Code, Is.EqualTo("remote"));
            Assert.That(record.HasPendingChanges, Is.False);
        });

        [UnityTest]
        public IEnumerator ManagedCollection_stale_read_does_not_overwrite_newer_save_or_pending_edits() => QueueRun(async () =>
        {
            var server = new ManagedServer();
            var old = server.Row.ToString();
            using var adapter = new PlayServDataSubscriptionAdapter(server.Bus, new FakeLogger(), new NewtonsoftJsonCodec());
            using var live = (IPlayServManagedCollection<InventoryItem>)await OpenManaged(adapter, server.Set);
            var record = live.Items.Single();
            record.Value.Code = "saved"; await record.SaveAsync();
            var tag = record.ETag;
            record.Value.Code = "pending";
            server.Http.DataHandler = (request, ct) => Task.FromResult(new PlayServRuntimeDataResponse(200, old, "\"v1\"", null));
            await live.RefreshAsync();
            Assert.That(record.Value.Code, Is.EqualTo("pending"));
            Assert.That(record.ETag, Is.EqualTo(tag));
            Assert.That(record.HasPendingChanges, Is.True);
        });

        [UnityTest]
        public IEnumerator ManagedCollection_unversioned_read_and_missing_record_preserve_last_good_state() => QueueRun(async () =>
        {
            var server = new ManagedServer();
            using var adapter = new PlayServDataSubscriptionAdapter(server.Bus, new FakeLogger(), new NewtonsoftJsonCodec());
            using var live = (IPlayServManagedCollection<InventoryItem>)await OpenManaged(adapter, server.Set);
            var record = live.Items.Single(); var tag = record.ETag;
            var body = (JObject)server.Row.DeepClone(); body.Remove("updated_at"); body["code_wire"] = "invalid";
            server.Http.DataHandler = (request, ct) => Task.FromResult(new PlayServRuntimeDataResponse(200, body.ToString(), null, null));
            await CaptureExceptionAsync<PlayServDataException>(() => live.RefreshAsync());
            Assert.That(record.ETag, Is.EqualTo(tag)); Assert.That(record.Value.Code, Is.EqualTo("old"));
            server.Http.DataHandler = (request, ct) => throw HttpError(404, "record_not_found", "{\"code\":\"record_not_found\"}");
            await CaptureExceptionAsync<PlayServDataException>(() => live.RefreshAsync());
            Assert.That(record.IsDeleted, Is.False, "Missing or invisible is not confirmed deletion.");
            Assert.That(live.Items.Single(), Is.SameAs(record));
        });

        [UnityTest]
        public IEnumerator ManagedCollection_dispose_during_read_prevents_late_changes() => QueueRun(async () =>
        {
            var server = new ManagedServer();
            using var adapter = new PlayServDataSubscriptionAdapter(server.Bus, new FakeLogger(), new NewtonsoftJsonCodec());
            var live = (IPlayServManagedCollection<InventoryItem>)await OpenManaged(adapter, server.Set);
            var retained = live.Items.Single(); var value = retained.Value;
            var gate = new TaskCompletionSource<PlayServRuntimeDataResponse>();
            var started = false;
            server.Http.DataHandler = (request, ct) => { started = true; return gate.Task; };
            var changes = 0; live.Changed += _ => changes++;
            server.Change("late", "Late"); server.Bus.Push();
            await QueueUntil(() => started);
            live.Dispose();
            gate.SetResult(new PlayServRuntimeDataResponse(200, server.Row.ToString(), server.Tag, null));
            await Task.Yield(); await Task.Yield();
            Assert.That(value.Code, Is.EqualTo("old"));
            Assert.That(retained.Value, Is.SameAs(value));
            Assert.That(changes, Is.Zero);
            Assert.That(live.Items, Is.Empty);
            Assert.That(server.Bus.Closes, Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator ManagedCollection_offline_refresh_fails_promptly_without_sending() => QueueRun(async () =>
        {
            var server = new ManagedServer();
            using var adapter = new PlayServDataSubscriptionAdapter(server.Bus, new FakeLogger(), new NewtonsoftJsonCodec());
            using var live = await OpenManaged(adapter, server.Set);
            server.Bus.State = PlayServState.Offline;
            var refresh = live.RefreshAsync();
            await QueueUntil(() => refresh.IsCompleted);
            await CaptureExceptionAsync<Playserv.DataSubscription.Exceptions.DataSubscriptionException>(() => refresh);
            Assert.That(server.Bus.Refreshes, Is.Zero);
        });

        [UnityTest]
        public IEnumerator ManagedCollection_malformed_initial_snapshot_fails_without_hanging() => QueueRun(async () =>
        {
            var server = new ManagedServer();
            server.Bus.DataOverride = new Dictionary<string, object> { ["InventoryItem"] = new Dictionary<string, object>() };
            using var adapter = new PlayServDataSubscriptionAdapter(server.Bus, new FakeLogger(), new NewtonsoftJsonCodec());
            var opened = OpenManaged(adapter, server.Set);
            await QueueUntil(() => opened.IsCompleted);
            await CaptureExceptionAsync<Exception>(() => opened);
            Assert.That(server.Bus.Closes, Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator ManagedCollection_public_api_keeps_filters_and_rejects_projection_before_IO() => QueueRun(async () =>
        {
            var server = new ManagedServer();
            using var adapter = new PlayServDataSubscriptionAdapter(server.Bus, new FakeLogger(), new NewtonsoftJsonCodec());
            var set = BindManaged(adapter, server.Set);
            await CaptureExceptionAsync<PlayServQueryCapabilityException>(() => set.SubscribeManagedAsync(new PlayServRecordQuery<InventoryItem>().SelectFields(x => x.Code)));
            Assert.That(server.Http.AllRequests, Is.Empty);
            Assert.That(server.Bus.Opens, Is.Zero);
            using var live = await set.SubscribeManagedAsync(new PlayServRecordQuery<InventoryItem>().Where(x => x.Code == "old").WithLimit(20));
            Assert.That(server.Bus.LastOpen.Query, Does.Contain("code_wire").And.Contain("{ * }"));
            Assert.That(server.Bus.LastOpen.Variables["limit"], Is.EqualTo(20));
        });

        [UnityTest]
        public IEnumerator ManagedCollection_initial_cancellation_releases_lease_and_ignores_late_read() => QueueRun(async () =>
        {
            var server = new ManagedServer();
            using var adapter = new PlayServDataSubscriptionAdapter(server.Bus, new FakeLogger(), new NewtonsoftJsonCodec());
            var gate = new TaskCompletionSource<PlayServRuntimeDataResponse>();
            var started = false;
            server.Http.DataHandler = (request, ct) => { started = true; return gate.Task; };
            using var cancel = new CancellationTokenSource();
            var open = BindManaged(adapter, server.Set).SubscribeManagedAsync(ct: cancel.Token);
            await QueueUntil(() => started); cancel.Cancel();
            await CaptureExceptionAsync<OperationCanceledException>(() => open);
            gate.SetResult(new PlayServRuntimeDataResponse(200, server.Row.ToString(), server.Tag, null));
            Assert.That(server.Bus.Closes, Is.EqualTo(1));
        });

        private sealed class ImmutableManaged { public string Name => "immutable"; }
        private sealed class ExtensionManaged
        {
            [Newtonsoft.Json.JsonExtensionData]
            public IDictionary<string, JToken> Additional = new Dictionary<string, JToken>();
        }

        [UnityTest]
        public IEnumerator ManagedCollection_extension_data_is_rejected_before_io() => QueueRun(async () =>
        {
            var http = new FakeDataHttpClient();
            var set = CreateSet<ExtensionManaged>(http, "ent_inventory");
            await CaptureExceptionAsync<ArgumentException>(() => set.SubscribeManagedAsync());
            Assert.That(http.AllRequests, Is.Empty);
        });

        [UnityTest]
        public IEnumerator ManagedCollection_buffered_pre_reset_snapshot_is_not_hydrated_after_recovery() => QueueRun(async () =>
        {
            var server = new ManagedServer(); server.Bus.Epoch = "epoch-a";
            using var adapter = new PlayServDataSubscriptionAdapter(server.Bus, new FakeLogger(), new NewtonsoftJsonCodec());
            var registry = (TransportSubscriptionRegistry)typeof(PlayServDataSubscriptionAdapter)
                .GetField("_transportRegistry", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(adapter);
            var lease = await registry.AcquireAsync("InventoryItem { * }", null, "InventoryItem", CancellationToken.None, bufferUntilActivated: true);
            server.Bus.Reset("epoch-a");
            var fresh = RecordJson("rec_2", "fresh", "Fresh", "2026-08-18T00:00:00Z");
            server.Bus.Rows = () => new[] { new NewtonsoftJsonCodec().ParseToPlainValue(fresh) };
            server.Bus.Push();
            var staleRead = false;
            server.Http.DataHandler = (request, ct) =>
            {
                if (request.RelativePath.EndsWith("/rec_1")) staleRead = true;
                return Task.FromResult(new PlayServRuntimeDataResponse(200,
                    request.RelativePath.EndsWith("/rec_1") ? server.Row.ToString() : fresh, server.Tag, null));
            };
            using var live = new PlayServManagedCollection<InventoryItem>(server.Set, adapter, lease, "InventoryItem");
            await live.InitializeAsync(CancellationToken.None);
            Assert.That(staleRead, Is.False, "Buffered pre-reset membership must not inherit the recovered lease generation.");
            Assert.That(live.Items.Single().Id, Is.EqualTo("rec_2"));
        });

        [UnityTest]
        public IEnumerator ManagedCollection_coalesced_push_retains_explicit_refresh_requirement() => QueueRun(async () =>
        {
            var server = new ManagedServer();
            using var adapter = new PlayServDataSubscriptionAdapter(server.Bus, new FakeLogger(), new NewtonsoftJsonCodec());
            using var live = await OpenManaged(adapter, server.Set);
            var gate = new TaskCompletionSource<PlayServRuntimeDataResponse>(); var reads = 0;
            server.Http.DataHandler = (request, ct) => ++reads == 1 ? gate.Task :
                Task.FromResult(new PlayServRuntimeDataResponse(200, server.Row.ToString(), server.Tag, null));
            var first = live.RefreshAsync();
            await QueueUntil(() => reads == 1);
            var second = live.RefreshAsync(); server.Bus.Push();
            gate.SetResult(new PlayServRuntimeDataResponse(200, server.Row.ToString(), server.Tag, null));
            await first; await second;
            Assert.That(reads, Is.EqualTo(2), "A non-forced push cannot erase the pending refresh's canonical read.");
        });

        [UnityTest]
        public IEnumerator ManagedCollection_frequent_pushes_do_not_cancel_initial_canonical_read() => QueueRun(async () =>
        {
            var server = new ManagedServer();
            using var adapter = new PlayServDataSubscriptionAdapter(server.Bus, new FakeLogger(), new NewtonsoftJsonCodec());
            using var cancel = new CancellationTokenSource();
            var gate = new TaskCompletionSource<PlayServRuntimeDataResponse>();
            var started = false; var readToken = CancellationToken.None;
            server.Http.DataHandler = (request, ct) => { started = true; readToken = ct; return gate.Task; };
            var opened = BindManaged(adapter, server.Set).SubscribeManagedAsync(ct: cancel.Token);
            try
            {
                await QueueUntil(() => started);
                server.Bus.Push();
                server.Change("next", "Next"); server.Bus.Push();
                Assert.That(readToken.IsCancellationRequested, Is.False, "Normal pushes must not starve an active synchronization.");
                gate.SetResult(new PlayServRuntimeDataResponse(200, server.Row.ToString(), server.Tag, null));
                using var live = await opened;
                Assert.That(live.Items, Has.Count.EqualTo(1));
            }
            finally { gate.TrySetResult(new PlayServRuntimeDataResponse(200, server.Row.ToString(), server.Tag, null)); cancel.Cancel(); }
        });

        [UnityTest]
        public IEnumerator ManagedCollection_new_epoch_reset_invalidates_inflight_read() => QueueRun(() => ManagedResetRead("epoch-b"));

        [UnityTest]
        public IEnumerator ManagedCollection_same_epoch_reset_invalidates_inflight_read() => QueueRun(() => ManagedResetRead("epoch-a"));

        private static async Task ManagedResetRead(string epoch)
        {
            var server = new ManagedServer(); server.Bus.Epoch = "epoch-a";
            using var adapter = new PlayServDataSubscriptionAdapter(server.Bus, new FakeLogger(), new NewtonsoftJsonCodec());
            using var live = await OpenManaged(adapter, server.Set);
            var record = live.Items.Single(); var value = record.Value;
            var handler = server.Http.DataHandler;
            var gate = new TaskCompletionSource<PlayServRuntimeDataResponse>();
            var started = false; var changes = 0;
            live.RecordChanged += _ => changes++;
            server.Http.DataHandler = (request, ct) => { started = true; return gate.Task; };
            server.Change("stale term", "Stale"); server.Bus.Push();
            await QueueUntil(() => started);
            server.Bus.Reset(epoch);
            Assert.That(live.State, Is.EqualTo(PlayServSubscriptionState.Reconnecting));
            gate.SetResult(new PlayServRuntimeDataResponse(200, server.Row.ToString(), server.Tag, null));
            await record.SaveAsync();
            Assert.That(value.Code, Is.EqualTo("old"));
            Assert.That(changes, Is.Zero);
            server.Http.DataHandler = handler;
            server.Change("new term", "New"); server.Bus.Push();
            await QueueUntil(() => value.Code == "new term");
            Assert.That(record.Value, Is.SameAs(value));
        }
        [UnityTest]
        public IEnumerator ManagedCollection_retained_reload_rejects_unversioned_state() => QueueRun(() => RejectUnversionedManagedAdoption(false));

        [UnityTest]
        public IEnumerator ManagedCollection_retained_save_rejects_unversioned_state() => QueueRun(() => RejectUnversionedManagedAdoption(true));

        private static async Task RejectUnversionedManagedAdoption(bool save)
        {
            var server = new ManagedServer();
            using var adapter = new PlayServDataSubscriptionAdapter(server.Bus, new FakeLogger(), new NewtonsoftJsonCodec());
            using var live = await OpenManaged(adapter, server.Set);
            var record = live.Items.Single(); var value = record.Value; var tag = record.ETag; var version = record.UpdatedAt;
            value.Code = "local";
            var invalid = (JObject)server.Row.DeepClone(); invalid.Remove("updated_at"); invalid["code_wire"] = "bad response";
            server.Http.DataHandler = (request, ct) => Task.FromResult(new PlayServRuntimeDataResponse(200, invalid.ToString(), null, null));
            await CaptureExceptionAsync<PlayServDataException>(() => save ? record.SaveAsync() : record.ReloadAsync());
            Assert.That(record.Value, Is.SameAs(value)); Assert.That(value.Code, Is.EqualTo("local"));
            Assert.That(record.ETag, Is.EqualTo(tag)); Assert.That(record.UpdatedAt, Is.EqualTo(version));
            Assert.That(record.HasPendingChanges, Is.True);
        }

        [UnityTest]
        public IEnumerator ManagedCollection_other_row_failure_does_not_hide_an_accepted_conflict() => QueueRun(async () =>
        {
            var server = new ManagedServer();
            var other = JObject.Parse(RecordJson("rec_2", "second", "Second"));
            var codec = new NewtonsoftJsonCodec();
            server.Bus.Rows = () => new[] { codec.ParseToPlainValue(server.Row.ToString()), codec.ParseToPlainValue(other.ToString()) };
            var failOther = false;
            server.Http.DataHandler = (request, ct) =>
            {
                var second = request.RelativePath.EndsWith("/rec_2");
                if (second && failOther) throw new InvalidOperationException("second row unavailable");
                return Task.FromResult(new PlayServRuntimeDataResponse(200, (second ? other : server.Row).ToString(), server.Tag, null));
            };
            using var adapter = new PlayServDataSubscriptionAdapter(server.Bus, new FakeLogger(), codec);
            using var live = await OpenManaged(adapter, server.Set);
            var record = live.Items.Single(item => item.Id == "rec_1");
            record.Value.Code = "unsaved";
            PlayServRecordRealtimeConflict<InventoryItem> conflict = null;
            live.Conflict += change => conflict = change;
            failOther = true; server.Change("remote", "Remote");
            await CaptureExceptionAsync<PlayServDataException>(() => live.RefreshAsync());
            Assert.That(record.Value.Code, Is.EqualTo("remote"));
            Assert.That(conflict, Is.Not.Null, "An applied backend-wins overwrite must report its lost local edits even if another row fails.");
            Assert.That(conflict.LocalValue.Code, Is.EqualTo("unsaved"));
        });

        [Test]
        public void ManagedCollection_immutable_dto_is_rejected_before_IO()
        {
            var http = new FakeDataHttpClient();
            var set = CreateSet<ImmutableManaged>(http, "ent_immutable");
            Assert.Throws<ArgumentException>(() => set.SubscribeManagedAsync().GetAwaiter().GetResult());
            Assert.That(http.AllRequests, Is.Empty);
        }

        private static PlayServRecordSet<InventoryItem> BindManaged(PlayServDataSubscriptionAdapter adapter, PlayServRecordSet<InventoryItem> set)
        {
            var client = (PlayServRecordsClient)set.GetType().GetField("_client", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(set);
            return new PlayServRecordSet<InventoryItem>(client, "ent_inventory", subscribeManagedAsync: adapter.SelectManagedCollectionAsync);
        }
        private static Task<IPlayServManagedCollection<InventoryItem>> OpenManaged(PlayServDataSubscriptionAdapter adapter, PlayServRecordSet<InventoryItem> set) =>
            BindManaged(adapter, set).SubscribeManagedAsync();

        private static IReadOnlyList<PlayServRecord<InventoryItem>> ManagedItems(object collection) =>
            (IReadOnlyList<PlayServRecord<InventoryItem>>)collection.GetType().GetProperty("Items").GetValue(collection);

        private sealed class ManagedServer
        {
            internal readonly FakeDataHttpClient Http = new FakeDataHttpClient();
            internal readonly ManagedCollectionBus Bus = new ManagedCollectionBus();
            internal readonly JObject Row = JObject.Parse(RecordJson("rec_1", "old", "Old"));
            internal int Version = 1;
            internal string Tag => "\"v" + Version + "\"";
            internal ManagedServer()
            {
                Http.ConfigureDefaultCatalogue("ent_inventory", nameof(InventoryItem), false);
                Bus.Rows = () => new object[] { new NewtonsoftJsonCodec().ParseToPlainValue(Row.ToString()) };
                Http.DataHandler = (request, ct) =>
                {
                    if (request.Method == "PATCH")
                    {
                        if (request.IfMatch != Tag) throw HttpError(412, "precondition_failed", "{\"code\":\"precondition_failed\"}");
                        foreach (var field in JObject.Parse(request.JsonBody).Properties()) Row[field.Name] = field.Value.DeepClone();
                        Change((string)Row["code_wire"], (string)Row["display_name"]);
                    }
                    return Task.FromResult(new PlayServRuntimeDataResponse(200, Row.ToString(), Tag, null));
                };
            }
            internal void Change(string code, string name)
            {
                Version++; Row["code_wire"] = code; Row["display_name"] = name;
                Row["updated_at"] = DateTimeOffset.Parse("2026-08-17T00:00:00Z").AddSeconds(Version).ToString("O");
            }
            internal PlayServRecordSet<InventoryItem> Set => CreateSet<InventoryItem>(Http, "ent_inventory");
        }

        private sealed class ManagedCollectionBus : IPlayServCommandBus
        {
            private readonly Dictionary<Type, List<Delegate>> _listeners = new Dictionary<Type, List<Delegate>>();
            internal Func<object[]> Rows;
            internal object DataOverride;
            internal string Epoch;
            internal ulong Sequence;
            internal DataSubscriptionRequest LastOpen;
            internal int Opens, Closes, Refreshes;
            public PlayServState State { get; set; } = PlayServState.Online;
            public IDisposable On<T>(Action<T> listener)
            {
                if (!_listeners.TryGetValue(typeof(T), out var list)) _listeners[typeof(T)] = list = new List<Delegate>();
                list.Add(listener); return new CallbackDisposable(() => list.Remove(listener));
            }
            public IDisposable OnCommand(string command, Action<object> listener) => EmptyDisposable.Instance;
            public Task SendAsync<T>(T command, string moduleName = null)
            {
                if (command is DataAuthorityContextRequest context)
                    Emit(new DataAuthorityContextResponse { RequestId = context.RequestId, Error = new DataSubscriptionError { ErrorCode = 32001 } });
                else if (command is DataSubscriptionRequest open)
                {
                    Opens++; LastOpen = open;
                    Emit(new DataSubscriptionResponse { RequestId = open.RequestId, Result = new DataSubscriptionResult { SubscriptionId = 93 } });
                    Push();
                }
                else if (command is DataSubscriptionRefreshRequest refresh) { Refreshes++; Push(refresh.RequestId); }
                else if (command is DataSubscriptionCloseRequest close)
                {
                    Closes++;
                    Emit(new DataSubscriptionCloseResponse { RequestId = close.RequestId, Result = new DataSubscriptionCloseResult { Success = true } });
                }
                return Task.CompletedTask;
            }
            internal void Push(long requestId = 0) => Emit(new DataSubscriptionUpdate
            {
                RequestId = requestId, DataSubscriptionId = 93, UpdateType = "Overwrite", IsCollection = true,
                Authority = Epoch == null ? null : new { Epoch, Sequence = (++Sequence).ToString(), Complete = true },
                Data = DataOverride ?? new Dictionary<string, object> { ["InventoryItem"] = Rows() }
            });
            internal void Reset(string epoch)
            {
                if (Epoch != epoch) Sequence = 0;
                Epoch = epoch;
                Emit(new DataSubscriptionUpdate { DataSubscriptionId = 93, UpdateType = "Reset", IsCollection = true,
                    Authority = new { Epoch, Sequence = (++Sequence).ToString(), Complete = false } });
            }
            private void Emit<T>(T message)
            {
                if (_listeners.TryGetValue(typeof(T), out var list))
                    foreach (var listener in list.ToArray()) ((Action<T>)listener)(message);
            }
        }
    }
}
