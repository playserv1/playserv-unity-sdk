using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.DataSubscription;
using Playserv.Modules;
using Playserv.Proxy.Common;
using Playserv.Proxy.Implementation;
using Playserv.Proxy.Interfaces;
using Playserv.Proxy.Logging;
using Playserv.Serialization;
using UnityEngine.TestTools;

namespace Playserv.Tests.Runtime
{
    public sealed class PlayServDataAuthorityTests
    {
        [Test]
        public void Governed_subscribe_negotiates_before_sending_the_context_epoch()
        {
            using var fixture = new WireFixture();
            using var handle = fixture.Open();
            Assert.That(fixture.Commands[0], Is.EqualTo("module_dataflow.DataAuthorityContextRequest"));
            Assert.That(fixture.Payload("DataAuthorityContextRequest")["Capabilities"].Select(value => value.ToString()), Is.EquivalentTo(new[] { "authoritative_state_v1" }));
            Assert.That(fixture.Payload("DataSubscriptionRequest")["Authority"]["Epoch"].ToString(), Is.EqualTo("epoch-a"));
        }

        [Test]
        public void Live_authoritative_overwrites_are_not_mistaken_for_restored_references()
        {
            using var fixture = new WireFixture();
            using var handle = fixture.Open();
            fixture.Update(10, 0, "new", "3");
            fixture.Update(10, 0, "stale", "2");
            Assert.That(handle.Items.Single().Code, Is.EqualTo("new"));
            Assert.That(fixture.Commands.Count(command => command.EndsWith("DataSubscriptionCloseRequest")), Is.Zero);
        }

        [Test]
        public void Deleted_restore_received_before_correlated_open_response_terminates_instead_of_resurrecting()
        {
            using var fixture = new WireFixture { DeleteBeforeOpenResponse = true };
            Assert.Throws<Playserv.DataSubscription.Exceptions.SubscriptionTerminatedException>(() => fixture.Open());
        }

        [UnityTest]
        public IEnumerator Missing_mutation_response_resolves_original_operation_instead_of_generic_timeout()
        {
            using var fixture = new WireFixture { LoseMutationResponse = true };
            var pending = fixture.Adapter.SendMutationAsync(10, "Inventory(id: $id) { Code }",
                new Dictionary<string, object> { ["id"] = "item" }, new Dictionary<string, object> { ["Code"] = "changed" });
            var deadline = UnityEngine.Time.realtimeSinceStartup + 12;
            while (!pending.IsCompleted && UnityEngine.Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(pending.IsCompleted, Is.True);
            Assert.That(pending.Exception, Is.Null);
            var mutation = fixture.Payload("DataMutationRequest");
            var status = fixture.Payload("DataAuthorityStatusRequest");
            Assert.That(status["OperationId"].ToString(), Is.EqualTo(mutation["Authority"]["OperationId"].ToString()));
            Assert.That(status["RetrySession"].ToString(), Is.EqualTo("opaque-session"));
            Assert.That(fixture.Logger.Messages, Does.Not.Contain("opaque-session"));
        }

        [Serializable]
        private sealed class Item { public string Code; }
        [Serializable]
        private sealed class NumericItem { public int Number; }

        [Test]
        public void Only_explicit_not_enabled_may_select_ungoverned_requests()
        {
            using var legacy = new WireFixture { ContextError = 32001 };
            using var handle = legacy.Open();
            Assert.That(legacy.Payload("DataSubscriptionRequest")["Authority"]?.Type,
                Is.Null.Or.EqualTo(Newtonsoft.Json.Linq.JTokenType.Null));
            using var unavailable = new WireFixture { ContextError = 32111 };
            var failure = Assert.Throws<Playserv.DataSubscription.Exceptions.DataSubscriptionException>(() => unavailable.Open());
            Assert.That(failure.ErrorCode, Is.EqualTo(32111));
            Assert.That(unavailable.Commands.Any(value => value.EndsWith(".DataSubscriptionRequest")), Is.False);
        }

        [Test]
        public void Reset_retains_stale_data_until_complete_new_epoch_baseline_and_discards_retired_epoch()
        {
            using var fixture = new WireFixture();
            using var handle = fixture.Open();
            fixture.Emit("DataSubscriptionUpdate", new { RequestId = 0, DataSubscriptionId = 10, UpdateType = "Reset", Authority = new { Epoch = "epoch-a", Sequence = "2", Complete = false, Reason = "base_unavailable" } });
            Assert.That(handle.State, Is.EqualTo(PlayServSubscriptionState.Reconnecting));
            Assert.That(handle.Items.Single().Code, Is.EqualTo("initial"));
            fixture.Epoch = "epoch-b";
            fixture.Update(10, 0, "baseline-b", "1");
            fixture.Epoch = "epoch-a";
            fixture.Update(10, 0, "retired", "99");
            Assert.That(handle.Items.Single().Code, Is.EqualTo("baseline-b"));
            Assert.That(handle.State, Is.EqualTo(PlayServSubscriptionState.Active));
        }

        [Test]
        public void Numeric_sequence_is_not_coerced_and_same_revision_higher_sequence_is_applied()
        {
            using var fixture = new WireFixture();
            using var handle = fixture.Open();
            fixture.Emit("DataSubscriptionUpdate", new { RequestId = 0, DataSubscriptionId = 10, UpdateType = "Overwrite", IsCollection = true,
                Data = new { Inventory = new[] { new { Code = "invalid" } } }, Authority = new { Epoch = "epoch-a", Sequence = 100, Revision = "1", Complete = true } });
            Assert.That(handle.Items.Single().Code, Is.EqualTo("initial"));
            fixture.Update(10, 0, "related-change", "2");
            Assert.That(handle.Items.Single().Code, Is.EqualTo("related-change"));
        }

        [TestCase(32101)] [TestCase(32102)] [TestCase(32103)] [TestCase(32104)] [TestCase(32105)]
        [TestCase(32106)] [TestCase(32107)] [TestCase(32108)] [TestCase(32109)] [TestCase(32110)]
        [TestCase(32111)] [TestCase(32112)] [TestCase(32113)] [TestCase(32114)]
        public void Authority_refusals_retain_exact_codes_and_are_not_success(int code)
        {
            using var fixture = new WireFixture { MutationRejection = code };
            var outcome = fixture.Adapter.SendWithOutcomeAsync("Inventory(id: $id) { Code }", new Dictionary<string, object> { ["id"] = "item" }, new { Code = "x" }, CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(outcome.State, Is.EqualTo(PlayServDataMutationState.Rejected));
            Assert.That(outcome.ErrorCode, Is.EqualTo(code));
        }

        [Test]
        public void Pending_resolves_without_reinvoking_mutator_or_changing_operation_envelope()
        {
            using var fixture = new WireFixture { MutationPending = true };
            var data = new Dictionary<string, object> { ["Code"] = "before" };
            var variables = new Dictionary<string, object> { ["id"] = "item" };
            var result = fixture.Adapter.SendWithOutcomeAsync("Inventory(id: $id) { Code }", variables, data, CancellationToken.None).GetAwaiter().GetResult();
            data["Code"] = "after";
            variables["id"] = "other";
            fixture.StatusKnown = false;
            fixture.MutationPending = false;
            var accepted = result.ResolveAsync().GetAwaiter().GetResult();
            Assert.That(accepted.State, Is.EqualTo(PlayServDataMutationState.Accepted));
            Assert.That(fixture.Payload("DataMutationRequest")["Data"]["Code"].ToString(), Is.EqualTo("before"));
            Assert.That(fixture.Payload("DataMutationRequest")["Variables"]["id"].ToString(), Is.EqualTo("item"));
            Assert.That(fixture.Payload("DataMutationRequest")["Authority"]["OperationId"].ToString(), Is.EqualTo(result.OperationId));
        }

        [Test]
        public void Identity_switch_does_not_resend_unknown_operation_or_apply_old_subscription_updates()
        {
            using var fixture = new WireFixture { MutationPending = true };
            using var handle = fixture.Open();
            var outcome = fixture.Adapter.SendWithOutcomeAsync("Inventory(id: $id) { Code }", null, new { Code = "before" }, CancellationToken.None).GetAwaiter().GetResult();
            var sent = fixture.Commands.Count;
            fixture.PlayerId = "other-player";
            var unresolved = outcome.ResolveAsync().GetAwaiter().GetResult();
            Assert.That(unresolved.State, Is.EqualTo(PlayServDataMutationState.Unknown));
            Assert.That(fixture.Commands.Count, Is.EqualTo(sent));
            fixture.Update(10, 0, "foreign", "2");
            Assert.That(handle.State, Is.EqualTo(PlayServSubscriptionState.Terminated));
            Assert.That(handle.Items.Single().Code, Is.EqualTo("initial"));
        }

        [Test]
        public void Ambiguous_send_failure_exposes_unknown_without_raw_exception_or_payload()
        {
            using var fixture = new WireFixture { FailMutationSend = true };
            var outcome = fixture.Adapter.SendWithOutcomeAsync("Inventory(id: $id) { Code }", null, new { Code = "secret-data-value" }, CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(outcome.State, Is.EqualTo(PlayServDataMutationState.Unknown));
            Assert.That(fixture.Logger.Messages, Does.Not.Contain("secret-data-value"));
        }

        [Test]
        public void Reconnection_after_context_reply_before_send_completion_fences_the_old_context()
        {
            using var fixture = new WireFixture { ReconnectDuringContextSend = true };
            Assert.Catch<OperationCanceledException>(() => fixture.Open());
            Assert.That(fixture.Commands.Any(value => value.EndsWith(".DataSubscriptionRequest")), Is.False);
        }

        [Test]
        public void Public_mutation_outcome_runs_mutator_once_and_installs_only_accepted_state()
        {
            using var fixture = new WireFixture { MutationPending = true, Row = true };
            using var handle = fixture.OpenRow();
            var calls = 0;
            var pending = handle.UpdateWithOutcomeAsync(value => { calls++; value.Code = "wish"; }).GetAwaiter().GetResult();
            Assert.That(pending.State, Is.EqualTo(PlayServDataMutationState.Pending));
            Assert.That(handle.Value.Code, Is.EqualTo("initial"));
            fixture.StatusKnown = false;
            fixture.MutationPending = false;
            var accepted = pending.ResolveAsync().GetAwaiter().GetResult();
            Assert.That(accepted.State, Is.EqualTo(PlayServDataMutationState.Accepted));
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(handle.Value.Code, Is.EqualTo("changed"));
            fixture.Emit("DataSubscriptionUpdate", new { RequestId = 0, DataSubscriptionId = 10,
                UpdateType = "Overwrite", IsCollection = false, EntityType = "Inventory", IdValue = "item",
                Data = new { Code = "authoritative" }, Authority = new { Epoch = "epoch-a", Sequence = "2", Revision = "2", Complete = true } });
            Assert.That(handle.Value.Code, Is.EqualTo("authoritative"));
        }

        [UnityTest]
        public IEnumerator Expired_original_session_is_not_replaced_when_resolving_pending_write()
        {
            using var fixture = new WireFixture { MutationPending = true, RetryWindowMs = 500 };
            var outcome = fixture.Adapter.SendWithOutcomeAsync("Inventory(id: $id) { Code }", null, new { Code = "x" }, CancellationToken.None).GetAwaiter().GetResult();
            var sent = fixture.Commands.Count;
            yield return new UnityEngine.WaitForSecondsRealtime(0.6f);
            var task = outcome.ResolveAsync();
            while (!task.IsCompleted) yield return null;
            Assert.That(task.Result.State, Is.EqualTo(PlayServDataMutationState.Unknown));
            Assert.That(task.Result.ErrorCode, Is.EqualTo(32103));
            Assert.That(fixture.Commands.Count, Is.EqualTo(sent));
        }

        [UnityTest]
        public IEnumerator Cancellation_and_disposal_release_negotiation_without_sending_subscription()
        {
            using var fixture = new WireFixture { HoldContextResponse = true };
            using var ct = new CancellationTokenSource();
            var pending = fixture.Adapter.SelectTypedCollectionAsync<Item>("Inventory { Code }", null, ct.Token);
            ct.Cancel();
            while (!pending.IsCompleted) yield return null;
            Assert.That(pending.IsCanceled, Is.True);
            fixture.Adapter.Dispose();
            fixture.Emit("DataAuthorityContextResponse", new { RequestId = 1, Result = new { Capability = "authoritative_state_v1", Epoch = "epoch-a", RetrySession = "opaque-session", RetryWindowMs = 10000 } });
            Assert.That(fixture.Commands.Any(value => value.EndsWith(".DataSubscriptionRequest")), Is.False);
        }

        [Test]
        public void Retired_epoch_orphan_delete_does_not_terminate_new_epoch_row()
        {
            using var fixture = new WireFixture { Row = true };
            using var handle = fixture.OpenRow();
            fixture.Epoch = "epoch-b";
            fixture.Update(10, 0, "new-epoch", "1");
            fixture.Emit("DataSubscriptionUpdate", new { RequestId = 0, DataSubscriptionId = 99, UpdateType = "Terminated", ErrorCode = 49001,
                EntityType = "Inventory", IdValue = "item", Authority = new { Epoch = "epoch-a", Revision = "9", Sequence = "9" } });
            Assert.That(handle.State, Is.EqualTo(PlayServSubscriptionState.Active));
            Assert.That(handle.Value.Code, Is.EqualTo("new-epoch"));
        }

        [Test]
        public void Row_overwrite_without_revision_cannot_replace_authoritative_state()
        {
            using var fixture = new WireFixture { Row = true };
            using var handle = fixture.OpenRow();
            fixture.Emit("DataSubscriptionUpdate", new { RequestId = 0, DataSubscriptionId = 10, UpdateType = "Overwrite",
                Data = new { Inventory = new { Code = "invalid" } }, Authority = new { Epoch = "epoch-a", Sequence = "2", Complete = true } });
            Assert.That(handle.Value.Code, Is.EqualTo("initial"));
        }

        [Test]
        public void Accepted_mutation_requires_complete_object_not_scalar_data()
        {
            using var fixture = new WireFixture { ScalarMutationData = true };
            var outcome = fixture.Adapter.SendWithOutcomeAsync("Inventory(id: $id) { Code }", null, new { Code = "x" }, CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(outcome.State, Is.EqualTo(PlayServDataMutationState.Unknown));
            Assert.That(outcome.ErrorCode, Is.EqualTo(40001));
            Assert.That(outcome.Data, Is.Null);
            Assert.That(outcome.ResolveAsync().GetAwaiter().GetResult().State, Is.EqualTo(PlayServDataMutationState.Accepted));
        }

        [Test]
        public void Retained_orphan_reference_tracking_is_bounded_with_orphan_frames()
        {
            using var fixture = new WireFixture();
            using var handle = fixture.Open();
            for (var id = 20; id < 170; id++) fixture.Update(id, 0, "orphan", "1");
            var registry = typeof(PlayServDataSubscriptionAdapter).GetField("_transportRegistry", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(fixture.Adapter);
            var references = (HashSet<long>)registry.GetType().GetField("_restoredAuthorityReferences", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(registry);
            Assert.That(references.Count, Is.LessThanOrEqualTo(128));
        }

        [UnityTest]
        public IEnumerator Subscription_callbacks_return_to_captured_unity_context()
        {
            using var fixture = new WireFixture();
            using var handle = fixture.Open();
            var context = SynchronizationContext.Current;
            SynchronizationContext delivered = null;
            var changed = false;
            handle.Changed += _ => { delivered = SynchronizationContext.Current; changed = true; };
            var emit = Task.Run(() => fixture.Update(10, 0, "background", "2"));
            while (!emit.IsCompleted || !changed) yield return null;
            Assert.That(delivered, Is.SameAs(context));
        }

        [UnityTest]
        public IEnumerator Pending_query_count_is_bounded_and_cancellation_stops_waiting()
        {
            using var fixture = new WireFixture { LoseMutationResponse = true };
            using var ct = new CancellationTokenSource();
            var tasks = new List<Task<PlayServDataMutationOutcome>>();
            for (var i = 0; i < 129; i++) tasks.Add(fixture.Adapter.SendWithOutcomeAsync("Inventory(id: $id) { Code }", null, new { Code = "x" }, ct.Token));
            Assert.That(tasks[128].IsCompleted, Is.True);
            Assert.That(tasks[128].Result.State, Is.EqualTo(PlayServDataMutationState.Unknown));
            Assert.That(tasks[128].Result.ErrorCode, Is.EqualTo(32106));
            Assert.That(fixture.Commands.Count(command => command.EndsWith(".DataMutationRequest")), Is.EqualTo(128));
            ct.Cancel();
            var all = Task.WhenAll(tasks);
            while (!all.IsCompleted) yield return null;
            Assert.That(all.IsCanceled, Is.False);
            Assert.That(all.Result.All(outcome => outcome.State == PlayServDataMutationState.Unknown), Is.True);
        }

        [UnityTest]
        public IEnumerator Nested_caller_objects_are_snapshotted_before_context_negotiation()
        {
            using var fixture = new WireFixture { HoldContextResponse = true };
            var nested = new Item { Code = "before" };
            var variables = new Dictionary<string, object> { ["filter"] = nested };
            var data = new Dictionary<string, object> { ["Nested"] = nested };
            var task = fixture.Adapter.SendWithOutcomeAsync("Inventory(id: $id) { Code }", variables, data, CancellationToken.None);
            nested.Code = "after";
            fixture.Emit("DataAuthorityContextResponse", new { RequestId = (long)fixture.Payload("DataAuthorityContextRequest")["RequestId"],
                Result = new { Capability = "authoritative_state_v1", Epoch = "epoch-a", RetrySession = "opaque-session", RetryWindowMs = 120000 } });
            while (!task.IsCompleted) yield return null;
            Assert.That(task.Exception, Is.Null);
            Assert.That(fixture.Payload("DataMutationRequest")["Variables"]["filter"]["Code"].ToString(), Is.EqualTo("before"));
            Assert.That(fixture.Payload("DataMutationRequest")["Data"]["Nested"]["Code"].ToString(), Is.EqualTo("before"));
        }

        [UnityTest]
        public IEnumerator Existing_update_during_authority_renewal_does_not_optimistically_change_value()
        {
            using var fixture = new WireFixture { Row = true };
            using var handle = fixture.OpenRow();
            fixture.HoldContextResponse = true;
            fixture.Adapter.OnConnected();
            var task = handle.UpdateAsync(value => value.Code = "wish");
            Assert.That(handle.Value.Code, Is.EqualTo("initial"));
            fixture.Adapter.Dispose();
            while (!task.IsCompleted) yield return null;
        }

        [Test]
        public void Initial_zero_request_id_snapshot_does_not_release_the_only_server_reference()
        {
            using var fixture = new WireFixture { InitialRequestIdZero = true };
            using var handle = fixture.Open();
            Assert.That(handle.Items.Single().Code, Is.EqualTo("initial"));
            Assert.That(fixture.Commands.Count(command => command.EndsWith("DataSubscriptionCloseRequest")), Is.Zero);
        }

        [Test]
        public void Two_projections_rebind_by_correlated_id_on_each_same_epoch_reconnect()
        {
            using var fixture = new WireFixture { Row = true };
            using var first = fixture.OpenRow();
            using var second = fixture.OpenRow("Inventory(id: $id) { Code Extra }");
            for (var attempt = 0; attempt < 2; attempt++)
            {
                fixture.ServerIdOffset = 10;
                fixture.SkipInitialUpdate = true;
                var restored = false;
                fixture.BeforeOpenResponse = (wire, _) =>
                {
                    if (restored) return;
                    restored = true;
                    wire.Update(20, 0, "first-" + attempt, "1");
                    wire.Update(21, 0, "second-" + attempt, "1");
                };
                fixture.Adapter.OnConnected();
                Assert.That(first.Value.Code, Is.EqualTo("first-" + attempt));
                Assert.That(second.Value.Code, Is.EqualTo("second-" + attempt));
                Assert.That(first.State, Is.EqualTo(PlayServSubscriptionState.Active));
                Assert.That(second.State, Is.EqualTo(PlayServSubscriptionState.Active));
                Assert.That(fixture.Commands.Count(command => command.EndsWith("DataSubscriptionCloseRequest")), Is.EqualTo((attempt + 1) * 2));
            }
        }

        [Test]
        public void Deleted_row_during_reconnect_gap_terminates_every_projection_without_resurrection()
        {
            using var fixture = new WireFixture { Row = true };
            using var first = fixture.OpenRow();
            using var second = fixture.OpenRow("Inventory(id: $id) { Code Extra }");
            fixture.ServerIdOffset = 10;
            fixture.BeforeOpenResponse = (wire, _) => wire.Emit("DataSubscriptionUpdate", new { RequestId = 0,
                DataSubscriptionId = 20, UpdateType = "Terminated", ErrorCode = 49001, EntityType = "Inventory", IdValue = "item",
                Authority = new { Epoch = "epoch-a", Revision = "2", Sequence = "2" } });
            fixture.Adapter.OnConnected();
            Assert.That(first.State, Is.EqualTo(PlayServSubscriptionState.Terminated));
            Assert.That(second.State, Is.EqualTo(PlayServSubscriptionState.Terminated));
        }

        [Test]
        public void Retired_epoch_failure_cannot_reset_current_epoch_view()
        {
            using var fixture = new WireFixture();
            using var handle = fixture.Open();
            fixture.Epoch = "epoch-b";
            fixture.Update(10, 0, "new", "1");
            fixture.Emit("DataSubscriptionUpdate", new { RequestId = 0, DataSubscriptionId = 10, UpdateType = "Terminated",
                ErrorCode = 32101, Authority = new { Epoch = "epoch-a", Sequence = "9", Revision = "9" } });
            Assert.That(handle.State, Is.EqualTo(PlayServSubscriptionState.Active));
            Assert.That(handle.Items.Single().Code, Is.EqualTo("new"));
        }

        [Test]
        public void Epoch_mismatch_on_reopen_negotiates_and_applies_new_epoch_without_reload()
        {
            using var fixture = new WireFixture();
            using var handle = fixture.Open();
            fixture.NextOpenEpochMismatch = true;
            fixture.Adapter.OnConnected();
            Assert.That(handle.State, Is.EqualTo(PlayServSubscriptionState.Active));
            Assert.That(fixture.Payload("DataSubscriptionRequest")["Authority"]["Epoch"].ToString(), Is.EqualTo("epoch-b"));
            Assert.That(fixture.Commands.Count(command => command.EndsWith("DataAuthorityContextRequest")), Is.EqualTo(3));
        }

        [Test]
        public void Context_send_failure_is_typed_and_does_not_expose_transport_payload()
        {
            using var fixture = new WireFixture { FailContextSend = true };
            var failure = Assert.Throws<Playserv.DataSubscription.Exceptions.DataSubscriptionException>(() => fixture.Open());
            Assert.That(failure.Message, Does.Not.Contain("secret-data-value"));
            Assert.That(fixture.Logger.Messages, Does.Not.Contain("secret-data-value"));
        }

        [TestCase(32101)] [TestCase(32103)] [TestCase(32104)] [TestCase(32111)]
        public void Refused_retry_does_not_claim_ambiguous_original_write_was_rejected(int code)
        {
            using var fixture = new WireFixture { FailMutationSend = true, StatusKnown = false };
            var first = fixture.Adapter.SendWithOutcomeAsync("Inventory(id: $id) { Code }", null, new { Code = "x" }, CancellationToken.None).GetAwaiter().GetResult();
            fixture.FailMutationSend = false;
            fixture.MutationRejection = code;
            var result = first.ResolveAsync().GetAwaiter().GetResult();
            Assert.That(result.State, Is.EqualTo(PlayServDataMutationState.Unknown));
            Assert.That(result.OperationId, Is.EqualTo(first.OperationId));
            Assert.That(result.ErrorCode, Is.EqualTo(code));
        }

        [UnityTest]
        public IEnumerator Canceling_the_only_acquire_stops_its_pending_open_before_late_context_reply()
        {
            using var fixture = new WireFixture { HoldContextResponse = true };
            using var ct = new CancellationTokenSource();
            var acquire = fixture.Adapter.AcquireTransportSubscriptionAsync("Inventory { Code }", null, "Inventory", ct.Token);
            var registry = typeof(PlayServDataSubscriptionAdapter).GetField("_transportRegistry", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(fixture.Adapter);
            var entries = (Dictionary<string, TransportSubscriptionRegistry.Entry>)registry.GetType().GetField("_entries", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(registry);
            var open = entries.Values.Single().OpenTask;
            ct.Cancel();
            while (!acquire.IsCompleted) yield return null;
            Assert.That(acquire.IsCanceled, Is.True);
            fixture.ReplyContext();
            while (!open.IsCompleted) yield return null;
            Assert.That(fixture.Commands.Count(command => command.EndsWith(".DataSubscriptionRequest")), Is.Zero);
        }

        [UnityTest]
        public IEnumerator Canceling_one_acquire_keeps_another_shared_acquire_alive()
        {
            using var fixture = new WireFixture { HoldContextResponse = true };
            using var ct = new CancellationTokenSource();
            var first = fixture.Adapter.AcquireTransportSubscriptionAsync("Inventory { Code }", null, "Inventory", ct.Token);
            var second = fixture.Adapter.AcquireTransportSubscriptionAsync("Inventory { Code }", null, "Inventory");
            ct.Cancel();
            while (!first.IsCompleted) yield return null;
            fixture.ReplyContext();
            while (!second.IsCompleted) yield return null;
            Assert.That(second.Exception, Is.Null);
            Assert.That(fixture.Commands.Count(command => command.EndsWith(".DataSubscriptionRequest")), Is.EqualTo(1));
            second.Result.Dispose();
        }

        [Test]
        public void New_epoch_reset_marks_view_stale_until_complete_baseline()
        {
            using var fixture = new WireFixture();
            using var handle = fixture.Open();
            fixture.Emit("DataSubscriptionUpdate", new { RequestId = 0, DataSubscriptionId = 10, UpdateType = "Reset",
                Authority = new { Epoch = "epoch-b", Sequence = "1", Complete = false, Reason = "base_unavailable" } });
            Assert.That(handle.State, Is.EqualTo(PlayServSubscriptionState.Reconnecting));
            fixture.Update(10, 0, "retired", "99");
            Assert.That(handle.Items.Single().Code, Is.EqualTo("initial"));
            fixture.Epoch = "epoch-b";
            fixture.Update(10, 0, "complete", "2");
            Assert.That(handle.State, Is.EqualTo(PlayServSubscriptionState.Active));
            Assert.That(handle.Items.Single().Code, Is.EqualTo("complete"));
        }

        [UnityTest]
        public IEnumerator Canceled_governed_open_closes_its_late_server_id_without_binding()
        {
            using var fixture = new WireFixture { HoldOpenResponse = true };
            using var ct = new CancellationTokenSource();
            var acquire = fixture.Adapter.AcquireTransportSubscriptionAsync("Inventory { Code }", null, "Inventory", ct.Token);
            var registry = typeof(PlayServDataSubscriptionAdapter).GetField("_transportRegistry", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(fixture.Adapter);
            var entries = (Dictionary<string, TransportSubscriptionRegistry.Entry>)registry.GetType().GetField("_entries", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(registry);
            var open = entries.Values.Single().OpenTask;
            ct.Cancel();
            while (!acquire.IsCompleted) yield return null;
            fixture.Emit("DataSubscriptionResponse", new { RequestId = (long)fixture.Payload("DataSubscriptionRequest")["RequestId"], Result = new { SubscriptionId = 88, Authority = new { Epoch = "epoch-a" } } });
            while (!open.IsCompleted) yield return null;
            Assert.That(entries, Is.Empty);
            Assert.That(fixture.Commands.Count(command => command.EndsWith("DataSubscriptionCloseRequest")), Is.EqualTo(1));
            Assert.That((long)fixture.Payload("DataSubscriptionCloseRequest")["SubscriptionId"], Is.EqualTo(88));
        }

        [TestCase(49001, true)] [TestCase(49001, false)] [TestCase(39002, false)]
        public void Active_terminal_server_frames_without_authority_end_the_bound_subscription(int code, bool row)
        {
            using var fixture = new WireFixture { Row = row };
            using var handle = row ? (IDisposable)fixture.OpenRow() : fixture.Open();
            fixture.Emit("DataSubscriptionUpdate", new { RequestId = 0, DataSubscriptionId = 10, UpdateType = "Terminated", ErrorCode = code });
            var state = row ? ((ISharedEntity<Item>)handle).State : ((ISharedCollection<Item>)handle).State;
            Assert.That(state, Is.EqualTo(PlayServSubscriptionState.Terminated));
        }

        [Test]
        public void Epoch_mismatch_receipt_uses_current_engine_epoch_without_losing_refusal()
        {
            using var fixture = new WireFixture { MutationRejection = 32101, ResponseEpoch = "epoch-b" };
            var refused = fixture.Adapter.SendWithOutcomeAsync("Inventory(id: $id) { Code }", null, new { Code = "x" }, CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(refused.State, Is.EqualTo(PlayServDataMutationState.Rejected));
            Assert.That(refused.ErrorCode, Is.EqualTo(32101));
            fixture.Epoch = "epoch-b";
            fixture.ResponseEpoch = null;
            fixture.MutationRejection = 0;
            var accepted = fixture.Adapter.SendWithOutcomeAsync("Inventory(id: $id) { Code }", null, new { Code = "y" }, CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(accepted.State, Is.EqualTo(PlayServDataMutationState.Accepted));
            Assert.That(fixture.Commands.Count(command => command.EndsWith("DataAuthorityContextRequest")), Is.EqualTo(2));
            Assert.That(fixture.Payload("DataMutationRequest")["Authority"]["Epoch"].ToString(), Is.EqualTo("epoch-b"));
        }

        [Test]
        public void Authority_unavailable_from_replacement_engine_preserves_typed_refusal()
        {
            using var fixture = new WireFixture { MutationRejection = 32111, ResponseEpoch = "epoch-b" };
            var refused = fixture.Adapter.SendWithOutcomeAsync("Inventory(id: $id) { Code }", null, new { Code = "x" }, CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(refused.State, Is.EqualTo(PlayServDataMutationState.Rejected));
            Assert.That(refused.ErrorCode, Is.EqualTo(32111));
        }

        [UnityTest]
        public IEnumerator Cancellation_after_mutation_send_preserves_unknown_operation_for_status_resolution()
        {
            using var fixture = new WireFixture { LoseMutationResponse = true };
            using var ct = new CancellationTokenSource();
            var task = fixture.Adapter.SendWithOutcomeAsync("Inventory(id: $id) { Code }", null, new { Code = "x" }, ct.Token);
            var operationId = fixture.Payload("DataMutationRequest")["Authority"]["OperationId"].ToString();
            ct.Cancel();
            while (!task.IsCompleted) yield return null;
            Assert.That(task.IsCanceled, Is.False);
            Assert.That(task.Result.State, Is.EqualTo(PlayServDataMutationState.Unknown));
            Assert.That(task.Result.OperationId, Is.EqualTo(operationId));
            var resolved = task.Result.ResolveAsync();
            while (!resolved.IsCompleted) yield return null;
            Assert.That(resolved.Result.State, Is.EqualTo(PlayServDataMutationState.Accepted));
            Assert.That(fixture.Payload("DataAuthorityStatusRequest")["OperationId"].ToString(), Is.EqualTo(operationId));
        }

        [UnityTest]
        public IEnumerator Base_unavailable_reopen_retries_at_bounded_rate_until_complete_baseline()
        {
            using var fixture = new WireFixture();
            using var handle = fixture.Open();
            fixture.RefuseOpens = 1;
            fixture.Emit("DataSubscriptionUpdate", new { RequestId = 0, DataSubscriptionId = 10, UpdateType = "Terminated", ErrorCode = 32107,
                Authority = new { Epoch = "epoch-a", Sequence = "2" } });
            var deadline = UnityEngine.Time.realtimeSinceStartup + 5;
            while (handle.State != PlayServSubscriptionState.Active && UnityEngine.Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(handle.State, Is.EqualTo(PlayServSubscriptionState.Active));
            Assert.That(fixture.Commands.Count(command => command.EndsWith(".DataSubscriptionRequest")), Is.EqualTo(3));
            Assert.That(handle.Items.Single().Code, Is.EqualTo("initial"));
        }

        [TestCase(false)] [TestCase(true)]
        public void Parked_stream_before_connected_callback_survives_old_sequence_and_buffer_reset(bool deleted)
        {
            using var fixture = new WireFixture { Row = true };
            using var handle = fixture.OpenRow();
            fixture.Update(10, 0, "old", "99");
            fixture.CurrentState = PlayServState.Reconnecting;
            fixture.ServerIdOffset = 10;
            fixture.SkipInitialUpdate = true;
            if (deleted)
                fixture.Emit("DataSubscriptionUpdate", new { RequestId = 0, DataSubscriptionId = 20, UpdateType = "Terminated", ErrorCode = 49001,
                    EntityType = "Inventory", IdValue = "item", Authority = new { Epoch = "epoch-a", Revision = "2", Sequence = "1" } });
            else fixture.Update(20, 0, "restored", "1");
            fixture.CurrentState = PlayServState.Online;
            fixture.Adapter.OnConnected();
            Assert.That(handle.State, Is.EqualTo(deleted ? PlayServSubscriptionState.Terminated : PlayServSubscriptionState.Active));
            Assert.That(handle.Value.Code, Is.EqualTo(deleted ? "old" : "restored"));
            if (!deleted) Assert.That(fixture.Commands.Count(command => command.EndsWith("DataSubscriptionCloseRequest")), Is.EqualTo(1));
        }

        [TestCase("null")] [TestCase("\"invalid\"")] [TestCase("{}")]
        public void Complete_collection_overwrite_requires_a_nonnull_array_root(string rootJson)
        {
            using var fixture = new WireFixture();
            using var handle = fixture.Open();
            fixture.Emit("DataSubscriptionUpdate", new { RequestId = 0, DataSubscriptionId = 10, UpdateType = "Overwrite", IsCollection = true,
                Data = new Dictionary<string, object> { ["Inventory"] = new NewtonsoftJsonCodec().ParseToPlainValue(rootJson) },
                Authority = new { Epoch = "epoch-a", Sequence = "2", Complete = true } });
            Assert.That(handle.Items.Single().Code, Is.EqualTo("initial"));
        }

        [Test]
        public void Malformed_numeric_data_does_not_reveal_payload_in_conversion_diagnostics()
        {
            using var fixture = new WireFixture();
            using var handle = fixture.Adapter.SelectTypedCollectionAsync<NumericItem>("Inventory { Number }", null, CancellationToken.None).GetAwaiter().GetResult();
            var messages = new StringBuilder();
            UnityEngine.Application.LogCallback capture = (condition, _, __) => messages.Append(condition);
            UnityEngine.Application.logMessageReceived += capture;
            try
            {
                LogAssert.Expect(UnityEngine.LogType.Error, "[PlayServ][data] Failed to map collection payload: JsonCodecException");
                fixture.Emit("DataSubscriptionUpdate", new { RequestId = 0, DataSubscriptionId = 10, UpdateType = "Overwrite", IsCollection = true,
                    Data = new { Inventory = new[] { new { Number = "secret-data-value" } } },
                    Authority = new { Epoch = "epoch-a", Sequence = "2", Complete = true } });
                Assert.That(messages.ToString(), Does.Not.Contain("secret-data-value"));
                Assert.That(handle.Items.Single().Number, Is.Zero);
            }
            finally { UnityEngine.Application.logMessageReceived -= capture; }
        }

        [UnityTest]
        public IEnumerator Connected_callback_before_online_state_allows_async_authority_renegotiation()
        {
            using var fixture = new WireFixture();
            using var handle = fixture.Open();
            fixture.HoldContextResponse = true;
            fixture.CurrentState = PlayServState.Reconnecting;
            fixture.Adapter.OnConnected();
            yield return null;
            fixture.CurrentState = PlayServState.Online;
            var deadline = UnityEngine.Time.realtimeSinceStartup + 5;
            while (fixture.Commands.Count(command => command.EndsWith("DataAuthorityContextRequest")) < 2 && UnityEngine.Time.realtimeSinceStartup < deadline) yield return null;
            fixture.ReplyContext();
            while (handle.State != PlayServSubscriptionState.Active && UnityEngine.Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(handle.State, Is.EqualTo(PlayServSubscriptionState.Active));
        }

        [UnityTest]
        public IEnumerator Restored_delete_queued_from_transport_thread_survives_connected_callback()
        {
            using var fixture = new WireFixture { Row = true };
            using var handle = fixture.OpenRow();
            fixture.Update(10, 0, "old", "99");
            fixture.CurrentState = PlayServState.Reconnecting;
            fixture.ServerIdOffset = 10;
            fixture.SkipInitialUpdate = true;
            Task.Run(() => fixture.Emit("DataSubscriptionUpdate", new { RequestId = 0, DataSubscriptionId = 20, UpdateType = "Terminated", ErrorCode = 49001,
                EntityType = "Inventory", IdValue = "item", Authority = new { Epoch = "epoch-a", Revision = "2", Sequence = "1" } })).GetAwaiter().GetResult();
            fixture.CurrentState = PlayServState.Online;
            fixture.Adapter.OnConnected();
            yield return null;
            Assert.That(handle.State, Is.EqualTo(PlayServSubscriptionState.Terminated));
        }

        [UnityTest]
        public IEnumerator Old_online_frame_queued_from_transport_thread_cannot_cross_connection_generation()
        {
            using var fixture = new WireFixture();
            using var handle = fixture.Open();
            Task.Run(() => fixture.Update(10, 0, "retired", "99")).GetAwaiter().GetResult();
            fixture.Adapter.OnConnected();
            yield return null;
            Assert.That(handle.Items.Single().Code, Is.EqualTo("initial"));
        }

        [UnityTest]
        public IEnumerator Canceled_open_and_replacement_sharing_server_id_close_only_canceled_reference()
        {
            using var fixture = new WireFixture { HoldOpenResponse = true };
            using var ct = new CancellationTokenSource();
            var first = fixture.Adapter.AcquireTransportSubscriptionAsync("Inventory { Code }", null, "Inventory", ct.Token);
            var firstId = (long)fixture.Payload("DataSubscriptionRequest")["RequestId"];
            ct.Cancel();
            while (!first.IsCompleted) yield return null;
            var second = fixture.Adapter.AcquireTransportSubscriptionAsync("Inventory { Code }", null, "Inventory");
            var secondId = (long)fixture.Payload("DataSubscriptionRequest")["RequestId"];
            fixture.Emit("DataSubscriptionResponse", new { RequestId = firstId, Result = new { SubscriptionId = 88, Authority = new { Epoch = "epoch-a" } } });
            fixture.Update(88, 0, "first-open", "1");
            fixture.Emit("DataSubscriptionResponse", new { RequestId = secondId, Result = new { SubscriptionId = 88, Authority = new { Epoch = "epoch-a" } } });
            fixture.Update(88, 0, "second-open", "2");
            while (!second.IsCompleted) yield return null;
            Assert.That(second.Exception, Is.Null);
            Assert.That(second.Result.State, Is.EqualTo(PlayServSubscriptionState.Active));
            Assert.That(fixture.Commands.Count(command => command.EndsWith("DataSubscriptionCloseRequest")), Is.EqualTo(1));
            second.Result.Dispose();
        }

        private sealed class WireFixture : IPlayServCommandBus, IPlayServRuntimeIdentity, ITransportImplementation, IDisposable
        {
            private readonly Observable _incoming = new Observable();
            private readonly NewtonsoftJsonCodec _json = new NewtonsoftJsonCodec();
            private readonly Transport _transport;
            private readonly List<Newtonsoft.Json.Linq.JObject> _frames = new List<Newtonsoft.Json.Linq.JObject>();
            public readonly CaptureLogger Logger = new CaptureLogger();
            public PlayServDataSubscriptionAdapter Adapter { get; }
            public bool DeleteBeforeOpenResponse;
            public bool LoseMutationResponse;
            public bool MutationPending;
            public bool FailMutationSend;
            public bool FailContextSend;
            public bool ReconnectDuringContextSend;
            public bool HoldContextResponse;
            public bool HoldOpenResponse;
            public bool Row;
            public bool ScalarMutationData;
            public bool InitialRequestIdZero;
            public bool NextOpenEpochMismatch;
            public int RefuseOpens;
            public bool SkipInitialUpdate;
            public int ServerIdOffset;
            public Action<WireFixture, long> BeforeOpenResponse;
            public long RetryWindowMs = 120000;
            public bool StatusKnown = true;
            public int ContextError;
            public int MutationRejection;
            public string Epoch = "epoch-a";
            public string ResponseEpoch;
            public string PlayerId { get; set; } = "player-a";
            public PlayServState CurrentState = PlayServState.Online;
            public PlayServState State => CurrentState;
            public List<string> Commands => _frames.Select(frame => (string)frame["Command"]).ToList();

            public WireFixture()
            {
                CommandTypeProviderRegistry.Register(new DataSubscriptionCommandTypeProvider());
                _transport = new Transport(this, new Playserv.Proxy.Implementation.JsonSerializer(_json,
                    new NewtonsoftCommandPayloadMapper()), new RequestIdGenerator(), Logger);
                Adapter = new PlayServDataSubscriptionAdapter(this, Logger, _json);
            }

            public ISharedCollection<Item> Open() => Adapter.SelectTypedCollectionAsync<Item>("Inventory { Code }", null, CancellationToken.None).GetAwaiter().GetResult();
            public ISharedEntity<Item> OpenRow(string query = "Inventory(id: $id) { Code }")
            {
                var variables = new Dictionary<string, object> { ["id"] = "item" };
                var lease = Adapter.AcquireTransportSubscriptionAsync(query, variables, "Inventory").GetAwaiter().GetResult();
                return new SharedEntity<Item>(Adapter, lease, null, query, variables);
            }
            public Newtonsoft.Json.Linq.JToken Payload(string command) => _frames.Last(frame => ((string)frame["Command"]).EndsWith("." + command))["Payload"];
            public void ReplyContext() => Emit("DataAuthorityContextResponse", new { RequestId = (long)Payload("DataAuthorityContextRequest")["RequestId"],
                Result = new { Capability = "authoritative_state_v1", Epoch, RetrySession = "opaque-session", RetryWindowMs } });
            public IDisposable On<T>(Action<T> onNext) => _transport.OnReceive<T>().Subscribe(new Observer<T>(onNext));
            public IDisposable OnCommand(string commandName, Action<object> onNext) => _transport.OnReceive(commandName).Subscribe(new Observer<object>(onNext));
            public Task SendAsync<T>(T command, string moduleName = null) => _transport.Send(command, moduleName);
            public Task<bool> Connect() => Task.FromResult(true);
            public void ResetConnection() { }
            public IObservable<byte[]> OnReceive() => _incoming;
            public Task Send(byte[] data)
            {
                var frame = Newtonsoft.Json.Linq.JObject.Parse(Encoding.UTF8.GetString(data));
                _frames.Add(frame);
                var command = ((string)frame["Command"]).Split('.').Last();
                var payload = frame["Payload"];
                var requestId = (long)payload["RequestId"];
                if (command == "DataAuthorityContextRequest")
                {
                    if (FailContextSend) throw new System.IO.IOException("secret-data-value");
                    if (HoldContextResponse) return Task.CompletedTask;
                    if (ContextError != 0) Emit("DataAuthorityContextResponse", new { RequestId = requestId, Error = new { ErrorCode = ContextError, Message = "refused" } });
                    else Emit("DataAuthorityContextResponse", new { RequestId = requestId, Result = new { Capability = "authoritative_state_v1", Epoch, RetrySession = "opaque-session", RetryWindowMs } });
                    if (ReconnectDuringContextSend) Adapter.OnConnected();
                }
                if (command == "DataSubscriptionRequest")
                {
                    if (HoldOpenResponse) return Task.CompletedTask;
                    if (RefuseOpens > 0)
                    {
                        RefuseOpens--;
                        Emit("DataSubscriptionResponse", new { RequestId = requestId, Error = new { ErrorCode = 32107, Message = "base unavailable" } });
                        return Task.CompletedTask;
                    }
                    if (NextOpenEpochMismatch)
                    {
                        NextOpenEpochMismatch = false;
                        Epoch = "epoch-b";
                        Emit("DataSubscriptionResponse", new { RequestId = requestId, Error = new { ErrorCode = 32101, Message = "epoch changed" } });
                        return Task.CompletedTask;
                    }
                    var serverId = 10 + ServerIdOffset + (((string)payload["Query"]).Contains("Extra") ? 1 : 0);
                    BeforeOpenResponse?.Invoke(this, serverId);
                    if (DeleteBeforeOpenResponse)
                        Emit("DataSubscriptionUpdate", new { RequestId = 0, DataSubscriptionId = 10, UpdateType = "Terminated", ErrorCode = 49001, EntityType = "Inventory", IdValue = "item", Authority = new { Epoch = "epoch-a", Revision = "2", Sequence = "1" } });
                    Emit("DataSubscriptionResponse", new { RequestId = requestId, Result = new { SubscriptionId = serverId, Authority = new { Epoch } } });
                    if (!DeleteBeforeOpenResponse && !SkipInitialUpdate)
                        Update(serverId, InitialRequestIdZero ? 0 : requestId, "initial", "1");
                }
                if (command == "DataSubscriptionCloseRequest")
                    Emit("DataSubscriptionCloseResponse", new { RequestId = requestId, Result = new { Success = true } });
                if (command == "DataMutationRequest" && !LoseMutationResponse)
                {
                    if (FailMutationSend) throw new System.IO.IOException("secret-data-value");
                    Emit("DataMutationResponse", new { RequestId = requestId, Result = new { Success = !MutationPending && MutationRejection == 0,
                        Authority = new { OperationId = (string)payload["Authority"]?["OperationId"], Epoch = ResponseEpoch ?? Epoch,
                            Outcome = MutationRejection != 0 ? "rejected" : MutationPending ? "pending" : "accepted", ErrorCode = MutationRejection == 0 ? (int?)null : MutationRejection, Revision = "2", Data = ScalarMutationData ? (object)"invalid" : new { Code = "changed" } } } });
                }
                if (command == "DataAuthorityStatusRequest")
                    Emit("DataAuthorityStatusResponse", new { RequestId = requestId, Result = new { Known = StatusKnown, Authority = new { OperationId = (string)payload["OperationId"], Epoch, Outcome = "accepted", Revision = "2", Data = new { Code = "changed" } } } });
                return Task.CompletedTask;
            }

            public void Update(long id, long requestId, string code, string sequence) => Emit("DataSubscriptionUpdate", new
            {
                RequestId = requestId, DataSubscriptionId = id, UpdateType = "Overwrite", IsCollection = !Row,
                EntityType = "Inventory", IdValue = Row ? "item" : "*", Data = Row ? (object)new { Code = code } : new { Inventory = new[] { new { Code = code } } },
                Authority = new { Epoch, Sequence = sequence, Revision = "1", Complete = true }
            });

            public void Emit(string command, object payload) => _incoming.Emit(Encoding.UTF8.GetBytes(_json.Serialize(new { Command = "module_dataflow." + command, Payload = payload })));
            public void Dispose() { Adapter.Dispose(); _transport.Dispose(); }
            void IDisposable.Dispose() => Dispose();
        }

        private sealed class Observer<T> : IObserver<T>
        {
            private readonly Action<T> _next;
            public Observer(Action<T> next) => _next = next;
            public void OnNext(T value) => _next(value);
            public void OnError(Exception error) { }
            public void OnCompleted() { }
        }

        private sealed class Observable : IObservable<byte[]>
        {
            private readonly List<IObserver<byte[]>> _observers = new List<IObserver<byte[]>>();
            public IDisposable Subscribe(IObserver<byte[]> observer) { _observers.Add(observer); return new Disposable(() => _observers.Remove(observer)); }
            public void Emit(byte[] bytes) { foreach (var observer in _observers.ToArray()) observer.OnNext(bytes); }
        }

        private sealed class Disposable : IDisposable
        {
            private Action _dispose;
            public Disposable(Action dispose) => _dispose = dispose;
            public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
        }

        private sealed class CaptureLogger : ILogger
        {
            public string Messages = string.Empty;
            public void Log(string message) => Messages += message;
            public void LogWarning(string message) => Messages += message;
            public void LogError(string message) => Messages += message;
        }
    }
}
