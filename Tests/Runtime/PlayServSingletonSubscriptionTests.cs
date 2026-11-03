using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Playserv.Data;
using Playserv.DataSubscription;
using Playserv.DataSubscription.Exceptions;
using Playserv.Http.Common;
using Playserv.Http.Interfaces;
using Playserv.Modules;
using Playserv.Proxy.Common;
using Playserv.Proxy.Implementation;
using Playserv.Proxy.Interfaces;
using Playserv.Proxy.Logging;
using Playserv.Runtime.Abstractions;
using Playserv.Serialization;
using Playserv.Wrapper;
using UnityEngine.TestTools;

namespace Playserv.Tests.Runtime
{
    public sealed class PlayServSingletonSubscriptionTests
    {
        [Serializable]
        public sealed class SettingsValue
        {
            [PlayServJsonName("title")] public string Title;
            [PlayServJsonName("limit")] public int Limit;
            [PlayServJsonName("nested")] public Nested Nested;
            [PlayServJsonName("featured")] public PlayServRecordRef<Owner> Owner;
        }

        [Serializable]
        public sealed class Nested { public string Label; }

        [Serializable]
        public sealed class Owner { [PlayServJsonName("label")] public string Label; }

        [Test]
        public void Public_singleton_subscription_is_read_only_and_separate_from_http_singleton()
        {
            var method = typeof(PlayServData).GetMethods().SingleOrDefault(value => value.Name == "SubscribeSingletonAsync");
            Assert.That(method, Is.Not.Null, "PSV-3205 requires the public singleton subscription entry point.");
            var handle = method.ReturnType.GenericTypeArguments.Single().GetGenericTypeDefinition();
            Assert.That(handle.Name, Is.EqualTo("IPlayServSingletonSubscription`1"));
            Assert.That(handle.GetMethods().Any(value => value.Name == "SaveAsync"), Is.False);
            Assert.That(handle.GetProperty("Value").CanWrite, Is.False);
            Assert.That(handle.GetProperty("ETag"), Is.Null);
        }

        [Test]
        public void Subscribe_installs_ws_defaults_without_http_row_read_or_write()
        {
            using var fixture = new Fixture();
            using var view = fixture.Open();
            Assert.That(view.Value.Title, Is.EqualTo("defaults"));
            Assert.That(view.EntityId, Is.EqualTo("ent_settings"));
            Assert.That(view.State, Is.EqualTo(PlayServSubscriptionState.Active));
            Assert.That(fixture.HttpPaths, Is.EqualTo(new[] { "data/tables" }));
            Assert.That((string)fixture.Payload("DataSubscriptionRequest")["Query"], Does.StartWith("SettingsValue(id:"));
            Assert.That(fixture.Commands, Does.Not.Contain("module_dataflow.DataMutationRequest"));
        }

        [Test]
        public void Push_uses_subscription_identity_not_persisted_row_id_without_http_reload()
        {
            using var fixture = new Fixture();
            using var view = fixture.Open();
            var events = new List<string>();
            view.OnChanged(value => { Assert.That(view.Value.Title, Is.EqualTo(value.Title)); events.Add(value.Title); });
            fixture.Update("operator", "2", idValue: "persisted-singleton-row");
            Assert.That(view.Value.Title, Is.EqualTo("operator"));
            Assert.That(events, Is.EqualTo(new[] { "operator" }));
            Assert.That(fixture.HttpPaths.Count, Is.EqualTo(1));
        }

        [Test]
        public void Value_and_each_notification_are_detached_from_retained_state()
        {
            using var fixture = new Fixture();
            using var view = fixture.Open();
            view.Value.Nested.Label = "caller edit";
            view.OnChanged(value => value.Nested.Label = "listener edit");
            string second = null;
            view.OnChanged(value => second = value.Nested.Label);
            fixture.Update("operator", "2");
            Assert.That(view.Value.Nested.Label, Is.EqualTo("canonical"));
            Assert.That(second, Is.EqualTo("canonical"));
        }

        [Test]
        public void Initial_reference_id_uses_records_mapping_and_retains_load_context()
        {
            using var fixture = new Fixture
            {
                InitialData = new { title = "defaults", limit = 8, featured = "rec_owner" },
                DataHandler = request => new PlayServRuntimeDataResponse(200,
                    "{\"id\":\"rec_owner\",\"label\":\"loaded\",\"created_at\":\"2026-10-22T00:00:00Z\",\"updated_at\":\"2026-10-22T00:00:00Z\"}", "\"owner-1\"", null)
            };
            using var view = fixture.Open();
            var reference = view.Value.Owner;
            Assert.That(reference.Id, Is.EqualTo("rec_owner"));
            Assert.That(reference.State, Is.EqualTo(PlayServRecordRefState.Unloaded));
            var loaded = reference.LoadAsync().GetAwaiter().GetResult();
            Assert.That(loaded.Value.Label, Is.EqualTo("loaded"));
            Assert.That(view.Value.Owner.State, Is.EqualTo(PlayServRecordRefState.Unloaded));
            Assert.That(fixture.HttpPaths.Last(), Does.Contain("ent_owners").And.Contain("rec_owner"));
        }

        [Test]
        public void Expanded_reference_push_and_notifications_are_defensive_snapshots()
        {
            using var fixture = new Fixture();
            using var view = fixture.Open();
            string observed = null;
            view.OnChanged(value => value.Owner.Value.Label = "listener edit");
            view.OnChanged(value => observed = value.Owner.Value.Label);
            fixture.UpdateData(new { title = "changed", limit = 8, featured = new { id = "rec_owner", label = "published" } }, "2");
            Assert.That(view.Value.Owner, Is.Not.Null);
            Assert.That(view.Value.Owner.Id, Is.EqualTo("rec_owner"));
            Assert.That(view.Value.Owner.State, Is.EqualTo(PlayServRecordRefState.Expanded));
            Assert.That(observed, Is.EqualTo("published"));
            view.Value.Owner.Value.Label = "caller edit";
            Assert.That(view.Value.Owner.Value.Label, Is.EqualTo("published"));
        }

        [Test]
        public void Sequence_orders_changes_and_reset_preserves_stale_value_until_new_baseline()
        {
            using var fixture = new Fixture();
            using var view = fixture.Open();
            var count = 0;
            view.OnChanged(_ => count++);
            fixture.Update("new", "3");
            fixture.Update("old", "2");
            fixture.Emit("DataSubscriptionUpdate", new { RequestId = 0, DataSubscriptionId = 10, UpdateType = "Reset", Authority = new { Epoch = "epoch-a", Sequence = "4", Complete = false } });
            Assert.That(view.State, Is.EqualTo(PlayServSubscriptionState.Reconnecting));
            Assert.That(view.Value.Title, Is.EqualTo("new"));
            fixture.Epoch = "epoch-b";
            fixture.Update("reset", "1");
            fixture.Epoch = "epoch-a";
            fixture.Update("retired", "99");
            Assert.That(view.Value.Title, Is.EqualTo("reset"));
            Assert.That(count, Is.EqualTo(2));
        }

        [Test]
        public void Ungoverned_patch_applies_to_snapshot_without_hidden_mutation()
        {
            using var fixture = new Fixture { Governed = false };
            using var view = fixture.Open();
            fixture.Emit("DataSubscriptionUpdate", new { RequestId = 0, DataSubscriptionId = 10, UpdateType = "Patch", Data = new[] { new { op = "replace", path = "/title", value = "patched" } } });
            Assert.That(view.Value.Title, Is.EqualTo("patched"));
            Assert.That(view.Value.Limit, Is.EqualTo(8));
            Assert.That(fixture.HttpPaths.Count, Is.EqualTo(1));
        }

        [Test]
        public void Ungoverned_live_overwrite_is_not_closed_as_a_restored_reference()
        {
            using var fixture = new Fixture { Governed = false };
            using var view = fixture.Open();
            fixture.Update("live", "2");
            Assert.That(view.Value.Title, Is.EqualTo("live"));
            Assert.That(fixture.Commands.Count(value => value.EndsWith("DataSubscriptionCloseRequest")), Is.Zero);
        }

        [Test]
        public void Ordinary_ungoverned_overwrite_without_identity_metadata_updates_live_view()
        {
            using var fixture = new Fixture { Governed = false };
            using var view = fixture.Open();
            fixture.Emit("DataSubscriptionUpdate", new { RequestId = 0, DataSubscriptionId = 10, UpdateType = "Overwrite", Data = new { title = "ordinary", limit = 5 } });
            Assert.That(view.Value.Title, Is.EqualTo("ordinary"));
            Assert.That(view.Value.Limit, Is.EqualTo(5));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Null_patch_or_null_operation_is_rejected_without_synthetic_change(bool nullOperation)
        {
            using var fixture = new Fixture { Governed = false };
            using var view = fixture.Open();
            var changes = 0;
            var failures = new List<DataSubscriptionException>();
            view.OnChanged(_ => changes++);
            view.OnError(failures.Add);
            fixture.Emit("DataSubscriptionUpdate", new { RequestId = 0, DataSubscriptionId = 10, UpdateType = "Patch", Data = nullOperation ? new object[] { null } : null });
            Assert.That(changes, Is.Zero);
            Assert.That(failures.Single().ErrorCode, Is.EqualTo(40001));
            Assert.That(view.Value.Title, Is.EqualTo("defaults"));
        }

        [Test]
        public void Second_view_after_patch_receives_complete_current_snapshot()
        {
            using var fixture = new Fixture { Governed = false };
            using var first = fixture.Open();
            fixture.Emit("DataSubscriptionUpdate", new { RequestId = 0, DataSubscriptionId = 10, UpdateType = "Patch", Data = new[] { new { op = "replace", path = "/title", value = "patched" } } });
            using var second = fixture.Open();
            Assert.That(second.Value.Title, Is.EqualTo("patched"));
            Assert.That(second.Value.Limit, Is.EqualTo(8));
        }

        [Test]
        public void Rejected_mixed_patch_cannot_become_baseline_for_second_view()
        {
            using var fixture = new Fixture { Governed = false };
            using var first = fixture.Open();
            var errors = new List<DataSubscriptionException>();
            first.OnError(errors.Add);
            fixture.Emit("DataSubscriptionUpdate", new
            {
                RequestId = 0,
                DataSubscriptionId = 10,
                UpdateType = "Patch",
                Data = new object[] { new { op = "replace", path = "/title", value = "rejected" }, null }
            });
            Assert.That(errors.Single().ErrorCode, Is.EqualTo(40001));
            Assert.That(first.Value.Title, Is.EqualTo("defaults"));
            using var second = fixture.Open();
            Assert.That(second.Value.Title, Is.EqualTo("defaults"));
        }

        [Test]
        public void Update_during_open_handoff_is_not_lost()
        {
            using var fixture = new Fixture { PushAfterInitial = true };
            using var view = fixture.Open();
            Assert.That(view.Value.Title, Is.EqualTo("racing-push"));
        }

        [Test]
        public void Shared_lease_keeps_second_view_live_when_first_is_disposed()
        {
            using var fixture = new Fixture();
            var first = fixture.Open();
            using var second = fixture.Open();
            var firstChanges = 0;
            first.OnChanged(_ => firstChanges++);
            first.Dispose();
            fixture.Update("survivor", "2");
            Assert.That(second.Value.Title, Is.EqualTo("survivor"));
            Assert.That(firstChanges, Is.Zero);
            Assert.That(fixture.Commands.Count(value => value.EndsWith("DataSubscriptionRequest")), Is.EqualTo(1));
            Assert.That(fixture.Commands.Count(value => value.EndsWith("DataSubscriptionCloseRequest")), Is.Zero);
            second.Dispose();
            Assert.That(fixture.Commands.Count(value => value.EndsWith("DataSubscriptionCloseRequest")), Is.EqualTo(1));
        }

        [Test]
        public void Known_read_denial_or_wrong_entity_never_opens_socket_subscription()
        {
            using var denied = new Fixture { ReadAllowed = false };
            Assert.Throws<PlayServDataAccessDeniedException>(() => denied.Open());
            Assert.That(denied.Commands, Is.Empty);
            using var wrong = new Fixture { Singleton = false };
            Assert.Throws<InvalidOperationException>(() => wrong.Open());
            Assert.That(wrong.Commands, Is.Empty);
        }

        [TestCase("scalar")]
        [TestCase("array")]
        [TestCase("null")]
        public void Invalid_initial_baseline_fails_and_closes_owned_lease(string shape)
        {
            using var fixture = new Fixture { InitialShape = shape, Governed = false };
            var pending = fixture.OpenAsync();
            Assert.That(pending.IsFaulted, Is.True, "Malformed baselines must fail immediately, not wait for a timeout.");
            var failure = Assert.Throws<DataSubscriptionException>(() => pending.GetAwaiter().GetResult());
            Assert.That(failure.ErrorCode, Is.EqualTo(40001));
            Assert.That(fixture.Commands.Count(value => value.EndsWith("DataSubscriptionCloseRequest")), Is.EqualTo(1));
        }

        [Test]
        public void Malformed_later_payload_retains_value_reports_safe_error_and_does_not_reload_http()
        {
            using var fixture = new Fixture();
            using var view = fixture.Open();
            var errors = new List<DataSubscriptionException>();
            view.OnError(errors.Add);
            fixture.UpdateData(new { title = "private-payload", limit = "private-payload" }, "2");
            Assert.That(view.Value.Title, Is.EqualTo("defaults"));
            Assert.That(errors.Count, Is.EqualTo(1));
            Assert.That(errors[0].ToString(), Does.Not.Contain("private-payload"));
            Assert.That(fixture.Logger.Messages, Does.Not.Contain("private-payload"));
            Assert.That(fixture.HttpPaths.Count, Is.EqualTo(1));
        }

        [Test]
        public void Server_refusal_preserves_code_without_optimistic_change()
        {
            using var fixture = new Fixture { Governed = false };
            using var view = fixture.Open();
            var errors = new List<DataSubscriptionException>();
            var changes = 0;
            view.OnError(errors.Add);
            view.OnChanged(_ => changes++);
            fixture.Emit("DataSubscriptionUpdate", new { RequestId = 0, DataSubscriptionId = 10, UpdateType = "Overwrite", ErrorCode = 32108, ErrorMessage = "private-payload" });
            Assert.That(errors.Single().ErrorCode, Is.EqualTo(32108));
            Assert.That(view.Value.Title, Is.EqualTo("defaults"));
            Assert.That(changes, Is.Zero);
        }

        [UnityTest]
        public IEnumerator Subscribe_waits_for_baseline_and_cancellation_releases_late_open()
        {
            using var fixture = new Fixture { HoldInitial = true };
            using var ct = new CancellationTokenSource();
            var pending = fixture.OpenAsync(ct.Token);
            yield return null;
            Assert.That(pending.IsCompleted, Is.False);
            ct.Cancel();
            while (!pending.IsCompleted) yield return null;
            Assert.That(pending.IsCanceled, Is.True);
            fixture.Update("late", "1");
            Assert.That(fixture.Commands.Count(value => value.EndsWith("DataSubscriptionCloseRequest")), Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator Missing_baseline_times_out_within_existing_budget_and_releases_lease()
        {
            using var fixture = new Fixture { HoldInitial = true };
            var pending = fixture.OpenAsync();
            var deadline = UnityEngine.Time.realtimeSinceStartup + PlayServDataSubscriptionAdapter.DataSubscriptionResponseTimeoutMs / 1000f + 3;
            while (!pending.IsCompleted && UnityEngine.Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(pending.IsCompleted, Is.True, "The baseline wait must use the bounded subscription response budget.");
            var failure = pending.Exception?.GetBaseException() as DataSubscriptionException;
            Assert.That(failure, Is.Not.Null);
            Assert.That(failure.UnifiedError.Code, Is.EqualTo(PlayServErrorCode.Timeout));
            Assert.That(fixture.Commands.Count(value => value.EndsWith("DataSubscriptionCloseRequest")), Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator Malformed_refresh_fails_without_replacing_last_good_snapshot()
        {
            using var fixture = new Fixture { HoldRefresh = true };
            using var view = fixture.Open();
            var pending = view.RefreshAsync();
            var id = (long)fixture.Payload("DataSubscriptionRefreshRequest")["RequestId"];
            fixture.UpdateData(new { title = "private-payload", limit = "private-payload" }, "2", id);
            var deadline = UnityEngine.Time.realtimeSinceStartup + 3;
            while (!pending.IsCompleted && UnityEngine.Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(pending.IsCompleted, Is.True);
            var failure = pending.Exception?.GetBaseException() as DataSubscriptionException;
            Assert.That(failure?.ErrorCode, Is.EqualTo(40001));
            Assert.That(failure?.ToString(), Does.Not.Contain("private-payload"));
            Assert.That(view.Value.Title, Is.EqualTo("defaults"));
        }

        [UnityTest]
        public IEnumerator Duplicate_refresh_frame_cannot_complete_before_an_accepted_snapshot_and_times_out()
        {
            using var fixture = new Fixture { HoldRefresh = true };
            using var view = fixture.Open();
            var pending = view.RefreshAsync();
            var id = (long)fixture.Payload("DataSubscriptionRefreshRequest")["RequestId"];
            fixture.Update("duplicate", "1", requestId: id);
            yield return null;
            Assert.That(pending.IsCompleted, Is.False);
            var deadline = UnityEngine.Time.realtimeSinceStartup + PlayServDataSubscriptionAdapter.DataSubscriptionRefreshTimeoutMs / 1000f + 3;
            while (!pending.IsCompleted && UnityEngine.Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(pending.IsCompleted, Is.True);
            var failure = pending.Exception?.GetBaseException() as DataSubscriptionException;
            Assert.That(failure, Is.Not.Null);
            Assert.That(failure.UnifiedError.Code, Is.EqualTo(PlayServErrorCode.Timeout));
            Assert.That(view.Value.Title, Is.EqualTo("defaults"));
        }

        [UnityTest]
        public IEnumerator Refresh_waits_for_accepted_snapshot_and_callbacks_run_on_unity_context()
        {
            using var fixture = new Fixture { HoldRefresh = true };
            using var view = fixture.Open();
            var thread = Thread.CurrentThread.ManagedThreadId;
            var callbackThread = -1;
            view.OnChanged(_ => callbackThread = Thread.CurrentThread.ManagedThreadId);
            var refresh = view.RefreshAsync();
            yield return null;
            Assert.That(refresh.IsCompleted, Is.False);
            var id = (long)fixture.Payload("DataSubscriptionRefreshRequest")["RequestId"];
            Task.Run(() => fixture.Update("fresh", "2", requestId: id)).GetAwaiter().GetResult();
            while (!refresh.IsCompleted) yield return null;
            Assert.That(refresh.Exception, Is.Null);
            Assert.That(view.Value.Title, Is.EqualTo("fresh"));
            Assert.That(callbackThread, Is.EqualTo(thread));
        }

        [UnityTest]
        public IEnumerator New_view_during_reset_waits_for_complete_baseline_instead_of_stale_cache()
        {
            using var fixture = new Fixture();
            using var first = fixture.Open();
            fixture.Emit("DataSubscriptionUpdate", new { RequestId = 0, DataSubscriptionId = 10, UpdateType = "Reset", Authority = new { Epoch = "epoch-a", Sequence = "2", Complete = false } });
            var second = fixture.OpenAsync();
            yield return null;
            Assert.That(second.IsCompleted, Is.False, "A retained stale snapshot must not initialize a new live view.");
            fixture.Epoch = "epoch-b";
            fixture.Update("fresh-baseline", "1");
            while (!second.IsCompleted) yield return null;
            Assert.That(second.Exception, Is.Null);
            using var view = second.Result;
            Assert.That(view.Value.Title, Is.EqualTo("fresh-baseline"));
        }

        [UnityTest]
        public IEnumerator New_view_after_ungoverned_reopen_waits_for_the_new_connections_baseline()
        {
            using var fixture = new Fixture { Governed = false };
            using var first = fixture.Open();
            fixture.ServerId = 20;
            fixture.HoldInitial = true;
            fixture.Adapter.OnConnected();
            var second = fixture.OpenAsync();
            yield return null;
            Assert.That(second.IsCompleted, Is.False, "The old connection's snapshot cannot initialize the new lease.");
            fixture.Update("new-connection", "1");
            while (!second.IsCompleted) yield return null;
            Assert.That(second.Exception, Is.Null);
            using var view = second.Result;
            Assert.That(view.Value.Title, Is.EqualTo("new-connection"));
        }

        [Test]
        public void Pre_cancelled_subscription_performs_no_http_or_websocket_io()
        {
            using var fixture = new Fixture();
            using var ct = new CancellationTokenSource();
            ct.Cancel();
            Assert.Catch<OperationCanceledException>(() => fixture.OpenAsync(ct.Token).GetAwaiter().GetResult());
            Assert.That(fixture.HttpPaths, Is.Empty);
            Assert.That(fixture.Commands, Is.Empty);
        }

        [UnityTest]
        public IEnumerator Refresh_cancellation_does_not_close_view_and_disposal_cancels_pending_refresh()
        {
            using var fixture = new Fixture { HoldRefresh = true };
            using var view = fixture.Open();
            using var ct = new CancellationTokenSource();
            var pending = view.RefreshAsync(ct.Token);
            ct.Cancel();
            while (!pending.IsCompleted) yield return null;
            Assert.That(pending.IsCanceled, Is.True);
            Assert.That(view.State, Is.EqualTo(PlayServSubscriptionState.Active));
            fixture.Update("still-live", "2");
            Assert.That(view.Value.Title, Is.EqualTo("still-live"));
            var disposed = view.RefreshAsync();
            view.Dispose();
            while (!disposed.IsCompleted) yield return null;
            Assert.That(disposed.IsCanceled, Is.True);
        }

        [Test]
        public void Close_precancellation_shared_leases_and_repetition_preserve_local_ownership()
        {
            using var fixture = new Fixture();
            using var first = fixture.Open();
            using var second = fixture.Open();
            using var ct = new CancellationTokenSource();
            ct.Cancel();
            Assert.Catch<OperationCanceledException>(() => first.CloseAsync(ct.Token).GetAwaiter().GetResult());
            Assert.That(first.State, Is.EqualTo(PlayServSubscriptionState.Active));
            Assert.That(first.CloseAsync().GetAwaiter().GetResult().IsSuccess, Is.True);
            Assert.That(first.State, Is.EqualTo(PlayServSubscriptionState.Closed));
            Assert.That(fixture.Commands.Count(command => command.EndsWith("DataSubscriptionCloseRequest")), Is.Zero);
            Assert.That(first.CloseAsync().GetAwaiter().GetResult().WasAlreadyClosed, Is.True);
            Assert.That(second.CloseAsync().GetAwaiter().GetResult().IsSuccess, Is.True);
            Assert.That(fixture.Commands.Count(command => command.EndsWith("DataSubscriptionCloseRequest")), Is.EqualTo(1));
        }

        [Test]
        public void Offline_close_releases_local_lease_even_when_server_close_is_unconfirmed()
        {
            using var fixture = new Fixture();
            using var view = fixture.Open();
            fixture.CurrentState = PlayServState.Offline;
            var result = view.CloseAsync().GetAwaiter().GetResult();
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(view.State, Is.EqualTo(PlayServSubscriptionState.Closed));
            Assert.That(view.CloseAsync().GetAwaiter().GetResult().WasAlreadyClosed, Is.True);
            Assert.That(fixture.Commands.Count(command => command.EndsWith("DataSubscriptionCloseRequest")), Is.Zero);
        }

        [UnityTest]
        public IEnumerator Close_cancellation_triggered_by_pending_refresh_cleanup_still_releases_lease()
        {
            using var fixture = new Fixture { HoldRefresh = true };
            using var view = fixture.Open();
            using var ct = new CancellationTokenSource();
            var refresh = view.RefreshAsync();
            var continuation = refresh.ContinueWith(_ => ct.Cancel(), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            Task<PlayServSubscriptionCloseResult> close = null;
            try { close = view.CloseAsync(ct.Token); } catch (OperationCanceledException) { }
            while (!continuation.IsCompleted || (close != null && !close.IsCompleted)) yield return null;
            Assert.That(ct.IsCancellationRequested, Is.True);
            if (close?.IsFaulted == true) Assert.That(close.Exception.GetBaseException(), Is.InstanceOf<OperationCanceledException>());
            Assert.That(view.State, Is.EqualTo(PlayServSubscriptionState.Closed));
            Assert.That(fixture.Commands.Count(command => command.EndsWith("DataSubscriptionCloseRequest")), Is.EqualTo(1));
            using var reopened = fixture.Open();
            Assert.That(fixture.Commands.Count(command => command.EndsWith("DataSubscriptionRequest")), Is.EqualTo(2));
        }

        [Test]
        public void Termination_reports_exact_code_once_and_ignores_later_updates()
        {
            using var fixture = new Fixture();
            using var view = fixture.Open();
            var errors = new List<DataSubscriptionException>();
            var terminations = 0;
            view.OnError(errors.Add);
            view.OnTerminated(() => terminations++);
            fixture.Emit("DataSubscriptionUpdate", new { RequestId = 0, DataSubscriptionId = 10, UpdateType = "Terminated", ErrorCode = 49001, Authority = new { Epoch = "epoch-a", Sequence = "2", Revision = "2" } });
            fixture.Update("late", "3");
            Assert.That(view.State, Is.EqualTo(PlayServSubscriptionState.Terminated));
            Assert.That(errors.Single().ErrorCode, Is.EqualTo(49001));
            Assert.That(terminations, Is.EqualTo(1));
            Assert.That(view.Value.Title, Is.EqualTo("defaults"));
        }

        [Test]
        public void Disposal_inside_error_listener_stops_remaining_lifecycle_callbacks()
        {
            using var fixture = new Fixture { Governed = false };
            using var view = fixture.Open();
            var extraCallbacks = 0;
            view.OnError(_ => view.Dispose());
            view.OnError(_ => extraCallbacks++);
            view.OnFailure(_ => extraCallbacks++);
            fixture.Emit("DataSubscriptionUpdate", new { RequestId = 0, DataSubscriptionId = 10, UpdateType = "Overwrite", ErrorCode = 32108, ErrorMessage = "refused" });
            Assert.That(extraCallbacks, Is.Zero);
        }

        [Test]
        public void Paused_handoff_buffers_patches_in_order_without_mutating_shared_snapshot()
        {
            using var fixture = new Fixture { Governed = false };
            var lease = fixture.AcquireBufferedLease();
            fixture.Emit("DataSubscriptionUpdate", new { RequestId = 0, DataSubscriptionId = 10, UpdateType = "Patch", Data = new[] { new { op = "replace", path = "/title", value = "first" } } });
            fixture.Emit("DataSubscriptionUpdate", new { RequestId = 0, DataSubscriptionId = 10, UpdateType = "Patch", Data = new[] { new { op = "replace", path = "/title", value = "second" } } });
            using var handle = new PlayServSingletonSubscription<SettingsValue>("ent_settings", fixture.Records, fixture.Adapter, lease);
            handle.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(handle.Value.Title, Is.EqualTo("second"));
            Assert.That(handle.Value.Limit, Is.EqualTo(8));
        }

        [Test]
        public void Handoff_overflow_fails_initialization_even_after_a_valid_baseline()
        {
            using var fixture = new Fixture();
            var lease = fixture.AcquireBufferedLease();
            for (var index = 0; index < 129; index++) fixture.Update("buffered", (index + 2).ToString());
            using var handle = new PlayServSingletonSubscription<SettingsValue>("ent_settings", fixture.Records, fixture.Adapter, lease);
            Assert.Throws<DataSubscriptionException>(() => handle.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult());
        }

        [Test]
        public void Adapter_disposal_before_lease_activation_cannot_install_retained_baseline()
        {
            using var fixture = new Fixture();
            var lease = fixture.AcquireBufferedLease();
            fixture.Adapter.Dispose();
            using var handle = new PlayServSingletonSubscription<SettingsValue>("ent_settings", fixture.Records, fixture.Adapter, lease);
            var pending = handle.InitializeAsync(CancellationToken.None);
            Assert.That(pending.IsCanceled || pending.IsFaulted, Is.True, "A closed lease must not initialize successfully.");
            Assert.That(handle.Value, Is.Null);
        }

        [UnityTest]
        public IEnumerator Termination_completes_all_pending_refreshes_without_reentrant_collection_mutation()
        {
            using var fixture = new Fixture { HoldRefresh = true };
            using var view = fixture.Open();
            var first = view.RefreshAsync();
            var firstId = (long)fixture.Payload("DataSubscriptionRefreshRequest")["RequestId"];
            fixture.Update("duplicate", "1", requestId: firstId);
            var second = view.RefreshAsync();
            var secondId = (long)fixture.Payload("DataSubscriptionRefreshRequest")["RequestId"];
            fixture.Update("duplicate", "1", requestId: secondId);
            yield return null;
            Assert.That(first.IsCompleted || second.IsCompleted, Is.False);
            var failures = 0;
            var terminations = 0;
            view.OnFailure(_ => failures++);
            view.OnTerminated(() => terminations++);
            Assert.DoesNotThrow(() => fixture.Emit("DataSubscriptionUpdate", new { RequestId = 0, DataSubscriptionId = 10, UpdateType = "Terminated", ErrorCode = 49001, Authority = new { Epoch = "epoch-a", Sequence = "2", Revision = "2" } }));
            var deadline = UnityEngine.Time.realtimeSinceStartup + 3;
            while ((!first.IsCompleted || !second.IsCompleted) && UnityEngine.Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(first.IsCompleted && second.IsCompleted, Is.True);
            Assert.That(first.IsFaulted && second.IsFaulted, Is.True);
            Assert.That(first.Exception.GetBaseException(), Is.InstanceOf<DataSubscriptionException>());
            Assert.That(second.Exception.GetBaseException(), Is.InstanceOf<DataSubscriptionException>());
            Assert.That(failures, Is.EqualTo(1));
            Assert.That(terminations, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator Adapter_disposal_drops_updates_already_queued_for_the_handles_context()
        {
            using var fixture = new Fixture();
            var original = SynchronizationContext.Current;
            var context = new QueuedContext();
            Task<View> pending;
            try
            {
                SynchronizationContext.SetSynchronizationContext(context);
                pending = fixture.OpenAsync();
            }
            finally { SynchronizationContext.SetSynchronizationContext(original); }
            var deadline = UnityEngine.Time.realtimeSinceStartup + 3;
            while (!pending.IsCompleted && UnityEngine.Time.realtimeSinceStartup < deadline)
            {
                context.Drain();
                yield return null;
            }
            Assert.That(pending.IsCompleted, Is.True);
            Assert.That(pending.Exception, Is.Null);
            using (var view = pending.Result)
            {
                var changes = 0;
                view.OnChanged(_ => changes++);
                fixture.Update("queued", "2");
                Assert.That(context.Count, Is.GreaterThan(0));
                fixture.Adapter.Dispose();
                context.Drain();
                Assert.That(changes, Is.Zero);
                Assert.That(view.Value.Title, Is.EqualTo("defaults"));
                Assert.That(view.State, Is.EqualTo(PlayServSubscriptionState.Closed));
            }
        }

        [Test]
        public void Reconnect_replaces_baseline_and_disposal_ignores_late_frames()
        {
            using var fixture = new Fixture();
            var view = fixture.Open();
            fixture.Epoch = "epoch-b";
            fixture.InitialTitle = "reconnected";
            fixture.Adapter.OnConnected();
            Assert.That(view.Value.Title, Is.EqualTo("reconnected"));
            var changes = 0;
            view.OnChanged(_ => changes++);
            view.Dispose();
            fixture.Update("late", "2");
            Assert.That(changes, Is.Zero);
        }

        private sealed class View : IDisposable
        {
            private readonly IPlayServSingletonSubscription<SettingsValue> _value;
            public View(IPlayServSingletonSubscription<SettingsValue> value) => _value = value;
            public SettingsValue Value => _value.Value;
            public string EntityId => _value.EntityId;
            public PlayServSubscriptionState State => _value.State;
            public void OnChanged(Action<SettingsValue> callback) => _value.Changed += callback;
            public void OnError(Action<DataSubscriptionException> callback) => _value.Error += callback;
            public void OnTerminated(Action callback) => _value.Terminated += callback;
            public void OnFailure(Action<PlayServError> callback) => _value.Failure += callback;
            public Task RefreshAsync(CancellationToken ct = default) => _value.RefreshAsync(ct);
            public Task<PlayServSubscriptionCloseResult> CloseAsync(CancellationToken ct = default) => _value.CloseAsync(ct);
            public void Dispose() => _value.Dispose();
        }

        private sealed class Fixture : IPlayServCommandBus, IPlayServRuntimeIdentity, ITransportImplementation, IPlayServRuntimeHttpClient, IDisposable
        {
            private readonly Incoming _incoming = new Incoming();
            private readonly NewtonsoftJsonCodec _json = new NewtonsoftJsonCodec();
            private readonly Transport _transport;
            private readonly List<JObject> _frames = new List<JObject>();
            private readonly PlayServRecordsClient _records;
            public PlayServRecordsClient Records => _records;
            public readonly CaptureLogger Logger = new CaptureLogger();
            public readonly List<string> HttpPaths = new List<string>();
            public PlayServDataSubscriptionAdapter Adapter { get; }
            public bool Governed = true, ReadAllowed = true, Singleton = true, PushAfterInitial, HoldInitial, HoldRefresh;
            public string Epoch = "epoch-a", InitialTitle = "defaults", InitialShape;
            public object InitialData;
            public Func<PlayServRuntimeDataRequest, PlayServRuntimeDataResponse> DataHandler;
            public long ServerId = 10;
            public PlayServState CurrentState = PlayServState.Online;
            public PlayServState State => CurrentState;
            public string PlayerId => "player-a";
            public List<string> Commands => _frames.Select(frame => (string)frame["Command"]).ToList();

            public Fixture()
            {
                CommandTypeProviderRegistry.Register(new DataSubscriptionCommandTypeProvider());
                _transport = new Transport(this, new Playserv.Proxy.Implementation.JsonSerializer(_json, new NewtonsoftCommandPayloadMapper()), new RequestIdGenerator(), Logger);
                Adapter = new PlayServDataSubscriptionAdapter(this, Logger, _json);
                _records = new PlayServRecordsClient(new PlayServSettings { ClientToken = "pk_test", BackendServerAddress = "https://singleton-" + Guid.NewGuid().ToString("N") + ".test" }, this, _json);
            }

            public View Open() => OpenAsync().GetAwaiter().GetResult();
            public TransportSubscriptionLease AcquireBufferedLease()
            {
                var registry = (TransportSubscriptionRegistry)typeof(PlayServDataSubscriptionAdapter)
                    .GetField("_transportRegistry", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(Adapter);
                var query = QueryBuilder.BuildKeyedAllFieldsQuery("SettingsValue", "default");
                return registry.AcquireAsync(query.Query, query.Variables, "SettingsValue", CancellationToken.None, bufferUntilActivated: true).GetAwaiter().GetResult();
            }
            public async Task<View> OpenAsync(CancellationToken ct = default)
            {
                var set = new PlayServRecordSet<SettingsValue>(_records, "ent_settings",
                    subscribeSingletonAsync: Adapter.SelectTypedSingletonAsync<SettingsValue>);
                return new View(await set.SubscribeSingletonAsync(ct));
            }

            public JToken Payload(string command) => _frames.Last(frame => ((string)frame["Command"]).EndsWith("." + command))["Payload"];
            public IDisposable On<T>(Action<T> onNext) => _transport.OnReceive<T>().Subscribe(new Observer<T>(onNext));
            public IDisposable OnCommand(string commandName, Action<object> onNext) => _transport.OnReceive(commandName).Subscribe(new Observer<object>(onNext));
            public Task SendAsync<T>(T command, string moduleName = null) => _transport.Send(command, moduleName);
            public Task<bool> Connect() => Task.FromResult(true);
            public void ResetConnection() { }
            public IObservable<byte[]> OnReceive() => _incoming;
            public Task Send(byte[] bytes)
            {
                var frame = JObject.Parse(Encoding.UTF8.GetString(bytes));
                _frames.Add(frame);
                var command = ((string)frame["Command"]).Split('.').Last();
                var requestId = (long)frame["Payload"]["RequestId"];
                if (command == "DataAuthorityContextRequest")
                {
                    if (Governed) Emit("DataAuthorityContextResponse", new { RequestId = requestId, Result = new { Capability = "authoritative_state_v1", Epoch, RetrySession = "private-session", RetryWindowMs = 120000 } });
                    else Emit("DataAuthorityContextResponse", new { RequestId = requestId, Error = new { ErrorCode = 32001, Message = "not enabled" } });
                }
                if (command == "DataSubscriptionRequest")
                {
                    Emit("DataSubscriptionResponse", new { RequestId = requestId, Result = new { SubscriptionId = ServerId, Authority = Governed ? new { Epoch } : null } });
                    if (!HoldInitial)
                    {
                        object data = InitialData ?? new { title = InitialTitle, limit = 8, nested = new { Label = "canonical" } };
                        if (InitialShape == "scalar") data = "private-payload";
                        if (InitialShape == "array") data = new[] { data };
                        if (InitialShape == "null") data = null;
                        UpdateData(data, "1", requestId);
                        if (PushAfterInitial) Update("racing-push", "2");
                    }
                }
                if (command == "DataSubscriptionRefreshRequest" && !HoldRefresh) Update("refreshed", "20", requestId: requestId);
                if (command == "DataSubscriptionCloseRequest") Emit("DataSubscriptionCloseResponse", new { RequestId = requestId, Result = new { Success = true } });
                return Task.CompletedTask;
            }

            public void Update(string title, string sequence, string idValue = "cosmetic-alias", long requestId = 0) => UpdateData(new { title, limit = 8, nested = new { Label = "canonical" } }, sequence, requestId, idValue);
            public void UpdateData(object data, string sequence, long requestId = 0, string idValue = "cosmetic-alias") => Emit("DataSubscriptionUpdate", new { RequestId = requestId, DataSubscriptionId = ServerId, UpdateType = "Overwrite", IsCollection = false, EntityType = "SettingsValue", IdValue = idValue, Data = data, Authority = Governed ? new { Epoch, Sequence = sequence, Revision = "1", Complete = true } : null });
            public void Emit(string command, object payload) => _incoming.Emit(Encoding.UTF8.GetBytes(_json.Serialize(new { Command = "module_dataflow." + command, Payload = payload })));
            public Task<PlayServRuntimeDataResponse> SendDataAsync(PlayServRuntimeDataRequest request, CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                HttpPaths.Add(request.RelativePath);
                if (request.RelativePath != "data/tables" && DataHandler != null) return Task.FromResult(DataHandler(request));
                Assert.That(request.RelativePath, Is.EqualTo("data/tables"), "A live singleton must not read or write the HTTP row.");
                return Task.FromResult(new PlayServRuntimeDataResponse(200, _json.Serialize(new { data = new object[] { new { entity_id = "ent_settings", name = "SettingsValue", singleton = Singleton, acl = new { client = new { read = ReadAllowed, write = true } } }, new { entity_id = "ent_owners", name = "Owner", singleton = false } } }), null, null));
            }
            public Task<string> GetLatestVersionAsync(string gameId, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<PlayerTokenBundleDto> SignInAnonAsync(string clientToken, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<PlayerRefreshResponseDto> RefreshAsync(string clientToken, string refreshToken, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<PlayerTokenBundleDto> LoginExternalAsync(string clientToken, PlayerExternalLoginRequestDto request, string playerAccessToken = null, CancellationToken ct = default) => throw new NotSupportedException();
            public Task SignOutAsync(string clientToken, string refreshToken, CancellationToken ct = default) => throw new NotSupportedException();
            public void Dispose() { Adapter.Dispose(); _transport.Dispose(); }
        }

        private sealed class Observer<T> : IObserver<T>
        {
            private readonly Action<T> _next;
            public Observer(Action<T> next) => _next = next;
            public void OnNext(T value) => _next(value);
            public void OnError(Exception error) { }
            public void OnCompleted() { }
        }
        private sealed class Incoming : IObservable<byte[]>
        {
            private readonly List<IObserver<byte[]>> _observers = new List<IObserver<byte[]>>();
            public IDisposable Subscribe(IObserver<byte[]> observer) { _observers.Add(observer); return new DisposeAction(() => _observers.Remove(observer)); }
            public void Emit(byte[] bytes) { foreach (var observer in _observers.ToArray()) observer.OnNext(bytes); }
        }
        private sealed class DisposeAction : IDisposable
        {
            private Action _action;
            public DisposeAction(Action action) => _action = action;
            public void Dispose() => Interlocked.Exchange(ref _action, null)?.Invoke();
        }
        private sealed class CaptureLogger : ILogger
        {
            public string Messages = string.Empty;
            public void Log(string message) => Messages += message;
            public void LogWarning(string message) => Messages += message;
            public void LogError(string message) => Messages += message;
        }
        private sealed class QueuedContext : SynchronizationContext
        {
            private readonly Queue<Action> _callbacks = new Queue<Action>();
            public int Count => _callbacks.Count;
            public override void Post(SendOrPostCallback callback, object state) => _callbacks.Enqueue(() => callback(state));
            public void Drain()
            {
                var original = Current;
                try
                {
                    SetSynchronizationContext(this);
                    while (_callbacks.Count > 0) _callbacks.Dequeue()();
                }
                finally { SetSynchronizationContext(original); }
            }
        }
    }
}
