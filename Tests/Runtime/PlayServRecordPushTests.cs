using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
using Playserv.Wrapper;
using UnityEngine.TestTools;

namespace Playserv.Tests.Runtime
{
    public sealed partial class PlayServTypedRecordsTests
    {
        [UnityTest] public IEnumerator RecordPush_complete_initial_snapshot_needs_no_second_get() => QueueRun(async () =>
        {
            using var f = new RecordPushFixture();
            f.Bus.IncludeVersion = true;
            await f.Open();
            Assert.That(f.Http.Requests.Count, Is.EqualTo(1));
            Assert.That(f.Record.ETag, Is.EqualTo(f.Tag));
        });

        [UnityTest] public IEnumerator RecordPush_complete_live_state_rotates_if_match_without_get() => QueueRun(async () =>
        {
            using var f = new RecordPushFixture(); await f.Open(); f.Http.Requests.Clear();
            var order = new List<string>();
            using var watch = f.Record.ObserveField("code_wire", _ => order.Add("field"));
            f.Record.Changed += _ => order.Add("record");
            f.Live.Changed += _ => order.Add("subscription");
            f.Change("remote"); await f.Live.RefreshAsync();
            Assert.That(f.Http.Requests, Is.Empty);
            Assert.That(f.Record.Value.Code, Is.EqualTo("remote"), $"State {f.Record.UpdatedAt:O}; server {f.Row}; token {f.Record.ETag}");
            Assert.That(f.Record.HasPendingChanges, Is.False);
            Assert.That(f.Record.Owner, Is.EqualTo("plr_1"));
            Assert.That(f.Record.CreatedAt, Is.EqualTo(DateTimeOffset.Parse("2026-08-16T00:00:00Z")));
            Assert.That(order, Is.EqualTo(new[] { "field", "record", "subscription" }));
            var tag = f.Tag;
            f.Record.Value.DisplayName = "saved"; await f.Record.SaveAsync();
            Assert.That(f.Http.Requests.Single().IfMatch, Is.EqualTo(tag));
        });

        [UnityTest] public IEnumerator RecordPush_complete_governed_snapshot_needs_no_get() => QueueRun(async () =>
        {
            using var f = new RecordPushFixture(); f.Bus.Epoch = "epoch-a"; await f.Open(); f.Http.Requests.Clear();
            f.Change("governed"); await f.Live.RefreshAsync();
            Assert.That(f.Http.Requests, Is.Empty);
            Assert.That(f.Record.Value.Code, Is.EqualTo("governed"));
            Assert.That(f.Record.ETag, Is.EqualTo(f.Tag));
        });

        [UnityTest] public IEnumerator RecordPush_missing_version_reads_canonical_state() => QueueRun(async () =>
        {
            using var f = new RecordPushFixture(); await f.Open(); f.Http.Requests.Clear();
            f.Change("canonical"); f.Bus.IncludeVersion = false;
            await f.Live.RefreshAsync();
            Assert.That(f.Http.Requests.Count, Is.EqualTo(1));
            Assert.That(f.Record.Value.Code, Is.EqualTo("canonical"));
            Assert.That(f.Record.ETag, Is.EqualTo(f.Tag));
        });

        [UnityTest] public IEnumerator RecordPush_duplicate_does_not_get_or_notify_or_discard_local_edits() => QueueRun(async () =>
        {
            using var f = new RecordPushFixture(); await f.Open();
            f.Change("remote"); await f.Live.RefreshAsync(); f.Http.Requests.Clear();
            var changes = 0; f.Live.Changed += _ => changes++;
            f.Record.Value.DisplayName = "unsaved";
            await f.Live.RefreshAsync();
            Assert.That(f.Http.Requests, Is.Empty);
            Assert.That(changes, Is.Zero);
            Assert.That(f.Record.Value.DisplayName, Is.EqualTo("unsaved"));
        });

        [UnityTest] public IEnumerator RecordPush_rest_derived_etag_uses_server_millisecond_precision() => QueueRun(async () =>
        {
            using var f = new RecordPushFixture();
            f.Row["updated_at"] = "2026-08-17T00:00:00.1234567Z";
            f.Http.DataHandler = (_, __) => Task.FromResult(new PlayServRuntimeDataResponse(200, f.Row.ToString(), null, null));
            var record = await f.Set.LoadAsync("rec_1");
            Assert.That(record.ETag, Is.EqualTo(f.Tag));
        });

        [UnityTest] public IEnumerator RecordPush_subscription_waits_for_delayed_initial_state() => QueueRun(async () =>
        {
            using var f = new RecordPushFixture();
            f.Record = await f.Set.LoadAsync("rec_1");
            f.Change("initial"); f.Bus.IncludeVersion = true; f.Bus.AutoPush = false;
            var opening = f.Record.SubscribeAsync();
            await QueueUntil(() => f.Bus.Opens == 1);
            Assert.That(opening.IsCompleted, Is.False, "The handle must await its initial baseline.");
            f.Bus.Push(); f.Live = await opening;
            Assert.That(f.Record.Value.Code, Is.EqualTo("initial"));
            Assert.That(f.Http.Requests.Count, Is.EqualTo(1));
        });

        [UnityTest] public IEnumerator RecordPush_full_update_reports_backend_wins_once_without_get() => QueueRun(async () =>
        {
            using var f = new RecordPushFixture(); await f.Open(); f.Http.Requests.Clear();
            var order = new List<string>(); PlayServRecordRealtimeConflict<InventoryItem> conflict = null;
            using var watch = f.Record.ObserveField("code_wire", change => { Assert.That(change.Previous, Is.EqualTo("local")); order.Add("field"); });
            f.Live.Conflict += value => { conflict = value; order.Add("conflict"); };
            f.Live.Changed += _ => order.Add("changed");
            f.Record.Value.Code = "local"; f.Change("remote"); await f.Live.RefreshAsync();
            Assert.That(f.Http.Requests, Is.Empty);
            Assert.That(order, Is.EqualTo(new[] { "field", "conflict", "changed" }));
            Assert.That(conflict.LocalValue.Code, Is.EqualTo("local"));
            Assert.That(conflict.RemoteValue.Code, Is.EqualTo("remote"));
            Assert.That(f.Record.HasPendingChanges, Is.False);
        });

        [UnityTest] public IEnumerator RecordPush_old_timestamp_never_replaces_newer_state() => QueueRun(async () =>
        {
            using var f = new RecordPushFixture(); await f.Open();
            f.Change("new"); await f.Live.RefreshAsync(); f.Http.Requests.Clear();
            var tag = f.Record.ETag; var changes = 0; f.Live.Changed += _ => changes++;
            f.Bus.DataOverride = new Dictionary<string, object> { ["code_wire"] = "old", ["updated_at"] = "2026-08-16T00:00:00Z" };
            await f.Live.RefreshAsync();
            Assert.That(f.Http.Requests, Is.Empty);
            Assert.That(f.Record.Value.Code, Is.EqualTo("new")); Assert.That(f.Record.ETag, Is.EqualTo(tag));
            Assert.That(changes, Is.Zero);
        });

        [UnityTest] public IEnumerator RecordPush_governed_sequence_orders_same_revision_and_rejects_duplicates() => QueueRun(async () =>
        {
            using var f = new RecordPushFixture(); f.Bus.Epoch = "epoch-a"; await f.Open(); f.Http.Requests.Clear();
            f.Change("first"); await f.Live.RefreshAsync();
            var sequence = f.Bus.Sequence; var changes = 0; f.Live.Changed += _ => changes++;
            f.Change("later"); f.Bus.Sequence = sequence - 1; f.Bus.Push();
            Assert.That(f.Record.Value.Code, Is.EqualTo("first"));
            await f.Live.RefreshAsync();
            Assert.That(f.Record.Value.Code, Is.EqualTo("later")); Assert.That(changes, Is.EqualTo(1));
            Assert.That(f.Http.Requests, Is.Empty);
        });

        [UnityTest] public IEnumerator RecordPush_reset_discards_inflight_read_and_does_not_report_refresh_success() => QueueRun(async () =>
        {
            using var f = new RecordPushFixture(); f.Bus.Epoch = "epoch-a"; await f.Open();
            var response = new TaskCompletionSource<PlayServRuntimeDataResponse>();
            f.Http.DataHandler = (_, __) => response.Task;
            f.Change("obsolete"); f.Bus.IncludeVersion = false; var refresh = f.Live.RefreshAsync();
            var old = f.Response();
            f.Bus.Reset("epoch-b"); response.SetResult(old);
            await QueueFailure<InvalidOperationException>(refresh);
            Assert.That(f.Record.Value.Code, Is.EqualTo("old"));
            f.Http.DataHandler = (_, __) => Task.FromResult(f.Response()); f.Http.Requests.Clear();
            f.Change("recovered"); f.Bus.IncludeVersion = true; f.Bus.Push();
            await QueueUntil(() => f.Record.Value.Code == "recovered");
            Assert.That(f.Http.Requests.Count, Is.EqualTo(1), "Recovery reads canonical state even when the first new frame is complete.");
            Assert.That(f.Record.ETag, Is.EqualTo(f.Tag));
        });

        [UnityTest] public IEnumerator RecordPush_reconnect_reads_once_then_resumes_direct_updates() => QueueRun(async () =>
        {
            using var f = new RecordPushFixture(); await f.Open(); f.Http.Requests.Clear();
            f.Change("reconnected"); f.Adapter.OnConnected();
            await QueueUntil(() => f.Record.Value.Code == "reconnected");
            Assert.That(f.Http.Requests.Count, Is.EqualTo(1));
            f.Http.Requests.Clear(); f.Change("live"); await f.Live.RefreshAsync();
            Assert.That(f.Http.Requests, Is.Empty);
        });

        [UnityTest] public IEnumerator RecordPush_failed_conditional_save_recovers_explicitly_with_fresh_token() => QueueRun(async () =>
        {
            using var f = new RecordPushFixture(); await f.Open();
            f.Record.Value.DisplayName = "local"; f.Change("other-writer"); f.Http.Requests.Clear();
            await QueueFailure<PlayServRecordConflictException>(f.Record.SaveAsync());
            Assert.That(f.Http.Requests.Count, Is.EqualTo(1));
            await f.Record.ReconcileAsync();
            Assert.That(f.Record.Value.DisplayName, Is.EqualTo("local"));
            var tag = f.Tag; await f.Record.SaveAsync();
            Assert.That(f.Http.Requests.Last().IfMatch, Is.EqualTo(tag));
        });

        [UnityTest] public IEnumerator RecordPush_missing_version_read_failure_preserves_saveable_state() => QueueRun(async () =>
        {
            using var f = new RecordPushFixture(); await f.Open(); var tag = f.Record.ETag;
            f.Change("untrusted"); f.Bus.IncludeVersion = false;
            f.Http.DataHandler = (_, __) => Task.FromResult(new PlayServRuntimeDataResponse(200,
                "{\"id\":\"rec_1\",\"code_wire\":\"bad\"}", null, null));
            await QueueFailure<PlayServDataException>(f.Live.RefreshAsync());
            Assert.That(f.Record.Value.Code, Is.EqualTo("old")); Assert.That(f.Record.ETag, Is.EqualTo(tag));
        });

        [UnityTest] public IEnumerator RecordPush_invalid_or_patch_payload_gets_canonical_value_before_publication() => QueueRun(async () =>
        {
            using var f = new RecordPushFixture(); await f.Open();
            var invalid = new object[] {
                new Dictionary<string, object> { ["updated_at"] = "not-a-date", ["code_wire"] = "invalid" },
                new Dictionary<string, object> { ["updated_at"] = "2026-09-01T00:00:00Z", ["id"] = "rec_other" },
                new object[] { new Dictionary<string, object> { ["op"] = "replace", ["path"] = "/code_wire", ["value"] = "patch" } }
            };
            for (var i = 0; i < invalid.Length; i++)
            {
                f.Change("canonical-" + i); f.Http.Requests.Clear(); f.Bus.DataOverride = invalid[i];
                f.Bus.UpdateType = i == 2 ? "Patch" : "Overwrite";
                await f.Live.RefreshAsync();
                Assert.That(f.Http.Requests.Count, Is.EqualTo(1));
                Assert.That(f.Record.Value.Code, Is.EqualTo("canonical-" + i));
                Assert.That(f.Record.ETag, Is.EqualTo(f.Tag));
            }
        });

        [UnityTest] public IEnumerator RecordPush_same_timestamp_ambiguous_fields_use_canonical_read() => QueueRun(async () =>
        {
            using var f = new RecordPushFixture(); await f.Open(); f.Http.Requests.Clear();
            f.Bus.DataOverride = new Dictionary<string, object> { ["code_wire"] = "ambiguous", ["updated_at"] = "2026-08-17T00:00:00Z" };
            await f.Live.RefreshAsync();
            Assert.That(f.Http.Requests.Count, Is.EqualTo(1));
            Assert.That(f.Record.Value.Code, Is.EqualTo("old"));
        });

        [UnityTest] public IEnumerator RecordPush_frame_waiting_behind_save_cannot_undo_the_write() => QueueRun(async () =>
        {
            using var f = new RecordPushFixture(); await f.Open(); f.Http.Requests.Clear();
            var gate = new TaskCompletionSource<PlayServRuntimeDataResponse>();
            f.Http.DataHandler = (_, __) => gate.Task;
            f.Record.Value.Code = "saved"; var save = f.Record.SaveAsync();
            f.Change("older-push"); f.Bus.Push();
            f.Change("saved"); gate.SetResult(f.Response()); await save;
            await f.Record.SynchronizeRealtimeAsync(CancellationToken.None);
            Assert.That(f.Record.Value.Code, Is.EqualTo("saved"));
            Assert.That(f.Http.Requests.Count, Is.EqualTo(2), "The queued older push must not issue a GET.");
        });

        [UnityTest] public IEnumerator RecordPush_dispose_suppresses_late_fallback_state() => QueueRun(async () =>
        {
            using var f = new RecordPushFixture(); await f.Open();
            var response = new TaskCompletionSource<PlayServRuntimeDataResponse>();
            f.Http.DataHandler = (_, __) => response.Task;
            f.Change("late"); f.Bus.IncludeVersion = false; var refresh = f.Live.RefreshAsync();
            f.Live.Dispose(); response.SetResult(f.Response());
            await QueueFailure<InvalidOperationException>(refresh);
            await Task.Yield(); Assert.That(f.Record.Value.Code, Is.EqualTo("old"));
        });

        [UnityTest] public IEnumerator RecordPush_authority_incomplete_frame_recovers_without_applying_its_data() => QueueRun(async () =>
        {
            using var f = new RecordPushFixture(); f.Bus.Epoch = "epoch-a"; await f.Open(); f.Http.Requests.Clear();
            f.Change("canonical");
            f.Bus.Emit(new DataSubscriptionUpdate { DataSubscriptionId = 81, UpdateType = "Overwrite",
                Data = new { code_wire = "untrusted" },
                Authority = new { Epoch = "epoch-a", Sequence = "2", Revision = "2", Complete = false } });
            await QueueUntil(() => f.Record.Value.Code == "canonical");
            Assert.That(f.Http.Requests.Count, Is.EqualTo(1)); Assert.That(f.Record.ETag, Is.EqualTo(f.Tag));
        });

        [UnityTest] public IEnumerator RecordPush_reference_fields_keep_bound_hydration_and_clean_parent() => QueueRun(async () =>
        {
            var http = ReferenceHttp(); var json = new NewtonsoftJsonCodec();
            var row = JObject.Parse(ParentJson("\"rec_child\""));
            var bus = new RecordPushBus { Entity = "RefParent", Id = "rec_parent", Fields = () => json.ParseToPlainValue(row.ToString()) as Dictionary<string, object> };
            using var adapter = new PlayServDataSubscriptionAdapter(bus, new FakeLogger(), json);
            var set = new PlayServRecordSet<RefParent>(new PlayServRecordsClient(Settings(), http, json), "ent_parents", subscribeRecordAsync: adapter.SelectTypedRecordAsync);
            http.EnqueueJson(200, row.ToString(), "\"p1\"");
            var record = await set.LoadAsync("rec_parent"); using var live = await record.SubscribeAsync(); http.Requests.Clear();
            row["child"] = "rec_new"; row["updated_at"] = "2026-11-22T00:00:00Z"; bus.IncludeVersion = true;
            await live.RefreshAsync();
            Assert.That(record.Value.Child.Id, Is.EqualTo("rec_new")); Assert.That(record.HasPendingChanges, Is.False);
            Assert.That(http.Requests, Is.Empty);
            http.EnqueueJson(200, ChildJson.Replace("rec_child", "rec_new"), "\"child\"");
            await record.Value.Child.LoadAsync();
            Assert.That(http.Requests.Single().RelativePath, Does.EndWith("/ent_children/records/rec_new"));
            Assert.That(record.HasPendingChanges, Is.False);
        });

        [UnityTest] public IEnumerator RecordPush_similarly_named_business_field_cannot_supply_system_version() => QueueRun(async () =>
        {
            using var f = new RecordPushFixture(); await f.Open(); f.Http.Requests.Clear();
            f.Bus.DataOverride = new Dictionary<string, object> { ["code_wire"] = "untrusted", ["Updated_At"] = "2026-11-22T00:00:00Z" };
            await f.Live.RefreshAsync();
            Assert.That(f.Http.Requests.Count, Is.EqualTo(1));
            Assert.That(f.Record.Value.Code, Is.EqualTo("old"));
        });

        [UnityTest] public IEnumerator RecordPush_shared_cached_patch_cannot_reuse_previous_overwrite_version() => QueueRun(async () =>
        {
            using var f = new RecordPushFixture(); await f.Open();
            var second = await f.Set.LoadAsync("rec_1");
            f.Change("first"); await f.Live.RefreshAsync();
            f.Change("patched");
            f.Bus.UpdateType = "Patch";
            f.Bus.DataOverride = new object[] { new Dictionary<string, object> { ["op"] = "replace", ["path"] = "/code_wire", ["value"] = "patched" } };
            await f.Live.RefreshAsync(); f.Http.Requests.Clear();
            using var secondLive = await second.SubscribeAsync();
            Assert.That(f.Http.Requests.Count, Is.EqualTo(1), "A synthesized overwrite cannot carry the previous frame's version into a new handle.");
            Assert.That(second.Value.Code, Is.EqualTo("patched")); Assert.That(second.ETag, Is.EqualTo(f.Tag));
        });

        [UnityTest] public IEnumerator RecordPush_initial_error_finishes_opening_without_waiting_for_timeout() => QueueRun(async () =>
        {
            using var f = new RecordPushFixture(); f.Bus.AutoPush = false;
            f.Record = await f.Set.LoadAsync("rec_1"); var opening = f.Record.SubscribeAsync();
            await QueueUntil(() => f.Bus.Opens == 1);
            f.Bus.Emit(new DataSubscriptionUpdate { DataSubscriptionId = 81, ErrorCode = 40001, Message = "snapshot refused" });
            await Task.Yield();
            Assert.That(opening.IsCompleted, Is.True, "An initial server refusal must finish the opening wait promptly.");
            await QueueFailure<Playserv.DataSubscription.Exceptions.DataSubscriptionException>(opening);
            Assert.That(f.Record.Value.Code, Is.EqualTo("old"));
        });

        private sealed class RecordPushFixture : IDisposable
        {
            internal readonly FakeDataHttpClient Http = new FakeDataHttpClient();
            internal readonly RecordPushBus Bus = new RecordPushBus();
            internal readonly JObject Row = JObject.Parse(RecordJson("rec_1", "old", "Old"));
            internal readonly PlayServDataSubscriptionAdapter Adapter;
            internal readonly PlayServRecordSet<InventoryItem> Set;
            internal PlayServRecord<InventoryItem> Record;
            internal IPlayServRecordSubscription<InventoryItem> Live;
            internal string Tag => PushTag((string)Row["updated_at"]);
            internal RecordPushFixture()
            {
                Bus.Fields = () => new NewtonsoftJsonCodec().ParseToPlainValue(Row.ToString()) as Dictionary<string, object>;
                Adapter = new PlayServDataSubscriptionAdapter(Bus, new FakeLogger(), new NewtonsoftJsonCodec());
                Set = CreateSet<InventoryItem>(Http, "ent_inventory", subscribeRecordAsync: Adapter.SelectTypedRecordAsync);
                Http.DataHandler = (request, ct) =>
                {
                    if (request.Method == "PATCH")
                    {
                        if (request.IfMatch != Tag) throw HttpError(412, "precondition_failed", "{\"code\":\"precondition_failed\"}");
                        foreach (var property in JObject.Parse(request.JsonBody).Properties()) Row[property.Name] = property.Value.DeepClone();
                        Change((string)Row["code_wire"]);
                    }
                    return Task.FromResult(Response());
                };
            }
            internal PlayServRuntimeDataResponse Response() => new PlayServRuntimeDataResponse(200, Row.ToString(), Tag, null);
            internal async Task Open() { Record = await Set.LoadAsync("rec_1"); Live = await Record.SubscribeAsync(); Bus.IncludeVersion = true; }
            internal void Change(string code)
            {
                Row["code_wire"] = code;
                Row["updated_at"] = DateTimeOffset.Parse((string)Row["updated_at"], CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal).AddSeconds(1).ToString("O");
            }
            public void Dispose() { Live?.Dispose(); Adapter.Dispose(); }
        }

        private static string PushTag(string instant)
        {
            var ticks = DateTimeOffset.Parse(instant, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal).UtcDateTime.Ticks;
            return "\"" + (ticks - ticks % TimeSpan.TicksPerMillisecond).ToString("x", CultureInfo.InvariantCulture) + "\"";
        }

        private sealed class RecordPushBus : IPlayServCommandBus
        {
            private readonly Dictionary<Type, List<Delegate>> _listeners = new Dictionary<Type, List<Delegate>>();
            internal Func<Dictionary<string, object>> Fields;
            internal string Epoch;
            internal ulong Sequence;
            internal bool IncludeVersion;
            internal bool AutoPush = true;
            internal object DataOverride;
            internal string UpdateType = "Overwrite";
            internal string Entity = "InventoryItem", Id = "rec_1";
            internal int Opens;
            public PlayServState State { get; set; } = PlayServState.Online;
            public IDisposable On<T>(Action<T> listener)
            {
                if (!_listeners.TryGetValue(typeof(T), out var list)) _listeners[typeof(T)] = list = new List<Delegate>();
                list.Add(listener); return new CallbackDisposable(() => list.Remove(listener));
            }
            public IDisposable OnCommand(string name, Action<object> listener) => EmptyDisposable.Instance;
            public Task SendAsync<T>(T command, string moduleName = null)
            {
                if (command is DataAuthorityContextRequest context)
                    Emit(new DataAuthorityContextResponse { RequestId = context.RequestId, Error = new DataSubscriptionError { ErrorCode = 32001 } });
                else if (command is DataSubscriptionRequest open)
                {
                    Opens++;
                    Emit(new DataSubscriptionResponse { RequestId = open.RequestId, Result = new DataSubscriptionResult { SubscriptionId = 81 } });
                    if (AutoPush) Push(open.RequestId);
                }
                else if (command is DataSubscriptionRefreshRequest refresh) Push(refresh.RequestId);
                else if (command is DataSubscriptionCloseRequest close)
                    Emit(new DataSubscriptionCloseResponse { RequestId = close.RequestId, Result = new DataSubscriptionCloseResult { Success = true } });
                return Task.CompletedTask;
            }
            internal void Push(long requestId = 0)
            {
                var data = Fields();
                data.Remove("id"); data.Remove("created_at"); data.Remove("owner");
                if (!IncludeVersion) data.Remove("updated_at");
                Emit(new DataSubscriptionUpdate { RequestId = requestId, DataSubscriptionId = 81, UpdateType = UpdateType,
                    EntityType = Entity, IdValue = Id, Data = DataOverride ?? data,
                    Authority = Epoch == null ? null : new { Epoch, Sequence = (++Sequence).ToString(), Revision = "1", Complete = true } });
            }
            internal void Emit<T>(T message)
            {
                if (_listeners.TryGetValue(typeof(T), out var list))
                    foreach (var listener in list.ToArray()) ((Action<T>)listener)(message);
            }
            internal void Reset(string epoch)
            {
                Epoch = epoch; Sequence = 1;
                Emit(new DataSubscriptionUpdate { DataSubscriptionId = 81, UpdateType = "Reset",
                    Authority = new { Epoch, Sequence = "1", Complete = false } });
            }
        }
    }
}
