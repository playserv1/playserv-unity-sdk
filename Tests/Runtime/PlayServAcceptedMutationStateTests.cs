using System;
using System.Collections.Generic;
using System.Collections;
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
using Playserv.DataSubscription.Exceptions;
using UnityEngine.TestTools;

namespace Playserv.Tests.Runtime
{
    public sealed class PlayServAcceptedMutationStateTests
    {
        [Test]
        public void Sequential_awaited_increments_use_accepted_state_without_waiting_for_push()
        {
            using var fixture = new WireFixture();
            using var entity = fixture.Open();
            entity.UpdateAsync(value => value.Number++).GetAwaiter().GetResult();
            entity.UpdateAsync(value => value.Number++).GetAwaiter().GetResult();
            Assert.That(fixture.Mutations.Select(value => (int)value["Data"]["Number"]), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(entity.Value.Number, Is.EqualTo(2));
        }

        [TestCase(0, 1)]
        [TestCase(7, 7)]
        public void Lower_label_push_is_refreshed_not_assumed_old_or_new(int pushed, int fresh)
        {
            using var fixture = new WireFixture { FreshNumber = fresh, FreshRevision = "0" };
            using var entity = fixture.Open();
            entity.UpdateAsync(value => value.Number++).GetAwaiter().GetResult();
            fixture.Push(pushed, "0");
            Assert.That(fixture.Refreshes.Count, Is.EqualTo(1));
            Assert.That(entity.Value.Number, Is.EqualTo(fresh));
            entity.UpdateAsync(value => value.Number++).GetAwaiter().GetResult();
            Assert.That((int)fixture.Mutations.Last()["Data"]["Number"], Is.EqualTo(fresh + 1));
        }

        [Test]
        public void Equal_revision_higher_sequence_update_is_not_suppressed()
        {
            using var fixture = new WireFixture();
            using var entity = fixture.Open();
            entity.UpdateAsync(value => value.Number++).GetAwaiter().GetResult();
            fixture.Push(9, "1");
            Assert.That(entity.Value.Number, Is.EqualTo(9));
            Assert.That(fixture.Refreshes, Is.Empty);
        }

        [Test]
        public void Intervening_push_is_not_overwritten_by_later_receipt()
        {
            using var fixture = new WireFixture { FreshNumber = 5, FreshRevision = "1" };
            using var entity = fixture.Open();
            fixture.BeforeMutationResponse = () => fixture.Push(5, "0");
            var outcome = entity.UpdateWithOutcomeAsync(value => value.Number++).GetAwaiter().GetResult();
            Assert.That(outcome.State, Is.EqualTo(PlayServDataMutationState.Accepted));
            Assert.That(entity.Value.Number, Is.EqualTo(5));
            Assert.That(fixture.Refreshes.Count, Is.EqualTo(1));
        }

        [Test]
        public void Failed_refresh_preserves_acceptance_and_blocks_next_mutator()
        {
            using var fixture = new WireFixture { FailRefresh = true };
            using var entity = fixture.Open();
            fixture.BeforeMutationResponse = () => fixture.Push(5, "0");
            var outcome = entity.UpdateWithOutcomeAsync(value => value.Number++).GetAwaiter().GetResult();
            Assert.That(outcome.State, Is.EqualTo(PlayServDataMutationState.Accepted));
            var invoked = false;
            Assert.Throws<DataSubscriptionException>(() => entity.UpdateAsync(value => invoked = true).GetAwaiter().GetResult());
            Assert.That(invoked, Is.False);
            Assert.That(fixture.Mutations.Count, Is.EqualTo(1));
        }

        [Test]
        public void Throwing_changed_does_not_change_acceptance_or_suppress_field_delivery()
        {
            using var fixture = new WireFixture();
            using var entity = fixture.Open();
            var values = new List<int>();
            using var watched = entity.ObserveField("Number", change => values.Add(change.Value.Number));
            entity.Changed += value => throw new InvalidOperationException("private-payload");
            Assert.That(entity.UpdateWithOutcomeAsync(value => value.Number++).GetAwaiter().GetResult().State,
                Is.EqualTo(PlayServDataMutationState.Accepted));
            entity.UpdateAsync(value => value.Number++).GetAwaiter().GetResult();
            Assert.That(values, Is.EqualTo(new[] { 0, 1, 2 }));
        }

        [UnityTest]
        public IEnumerator Polling_read_started_before_receipt_cannot_replace_accepted_state()
        {
            using var fixture = new WireFixture { HoldDataGet = true, FreshNumber = 1 };
            using var entity = fixture.OpenPolling();
            var initial = entity.RefreshAsync();
            yield return new UnityEngine.WaitUntil(() => fixture.Gets.Count == 1);
            entity.UpdateAsync(value => value.Number++).GetAwaiter().GetResult();
            fixture.HoldDataGet = false;
            fixture.ReplyGet(fixture.Gets[0], 0);
            yield return new UnityEngine.WaitUntil(() => initial.IsCompleted);
            Assert.That(entity.Value.Number, Is.EqualTo(1));
            Assert.That(fixture.Gets.Count, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator Fresh_poll_equal_to_a_rejected_old_read_is_not_suppressed()
        {
            using var fixture = new WireFixture { HoldDataGet = true, FreshNumber = 1, RelationOnly = true };
            using var entity = fixture.OpenPolling();
            var oldRead = entity.RefreshAsync();
            yield return new UnityEngine.WaitUntil(() => fixture.Gets.Count == 1);
            entity.UpdateAsync(value => value.Relation.Value++).GetAwaiter().GetResult();
            fixture.HoldDataGet = false;
            fixture.ReplyGet(fixture.Gets[0], 0);
            yield return new UnityEngine.WaitUntil(() => oldRead.IsCompleted);
            Assert.That(entity.Value.Relation.Value, Is.EqualTo(1));
            Assert.That(fixture.Gets.Count, Is.EqualTo(2));

            fixture.FreshNumber = 0;
            // The production coordinator coalesces explicit polling refreshes for one second.
            yield return new UnityEngine.WaitForSecondsRealtime(1.05f);
            var freshRead = entity.RefreshAsync();
            yield return new UnityEngine.WaitUntil(() => freshRead.IsCompleted);
            Assert.That(fixture.Gets.Count, Is.GreaterThanOrEqualTo(3));
            Assert.That(entity.Value.Relation.Value, Is.Zero);
        }

        [UnityTest]
        public IEnumerator Null_polling_reread_does_not_clear_the_fresh_state_barrier()
        {
            using var fixture = new WireFixture { HoldDataGet = true };
            using var entity = fixture.OpenPolling();
            var initial = entity.RefreshAsync();
            yield return new UnityEngine.WaitUntil(() => fixture.Gets.Count == 1);
            entity.UpdateAsync(value => value.Number++).GetAwaiter().GetResult();
            fixture.HoldDataGet = false;
            fixture.NullGet = true;
            fixture.ReplyGet(fixture.Gets[0], 0);
            yield return new UnityEngine.WaitUntil(() => initial.IsCompleted);
            Assert.That(entity.Value.Number, Is.EqualTo(1));
            Assert.Throws<DataSubscriptionException>(() => entity.UpdateAsync(value => value.Number++).GetAwaiter().GetResult());
            Assert.That(fixture.Mutations.Count, Is.EqualTo(1));
        }

        [TestCase("pending", PlayServDataMutationState.Pending)]
        [TestCase("rejected", PlayServDataMutationState.Rejected)]
        [TestCase("unknown", PlayServDataMutationState.Unknown)]
        public void Nonaccepted_wishes_are_never_installed(string state, PlayServDataMutationState expected)
        {
            using var fixture = new WireFixture { MutationState = state };
            using var entity = fixture.Open();
            var changes = 0;
            entity.Changed += value => changes++;
            var outcome = entity.UpdateWithOutcomeAsync(value => value.Number = 99).GetAwaiter().GetResult();
            Assert.That(outcome.State, Is.EqualTo(expected));
            Assert.That(entity.Value.Number, Is.Zero);
            Assert.That(changes, Is.Zero);
        }

        [Test]
        public void Resolve_installs_original_operation_once_without_running_mutator_again()
        {
            using var fixture = new WireFixture { MutationState = "pending", FreshNumber = 8 };
            using var entity = fixture.Open();
            var mutations = 0;
            var changes = new List<int>();
            entity.Changed += value => changes.Add(value.Number);
            var pending = entity.UpdateWithOutcomeAsync(value => { mutations++; value.Number = 8; }).GetAwaiter().GetResult();
            var accepted = pending.ResolveAsync().GetAwaiter().GetResult();
            accepted.ResolveAsync().GetAwaiter().GetResult();
            pending.ResolveAsync().GetAwaiter().GetResult();
            Assert.That(accepted.OperationId, Is.EqualTo(pending.OperationId));
            Assert.That(changes, Is.EqualTo(new[] { 8 }));
            Assert.That(mutations, Is.EqualTo(1));
            Assert.That(fixture.Mutations.Count, Is.EqualTo(1));
            Assert.That(fixture.Statuses.All(request => (string)request["OperationId"] == pending.OperationId), Is.True);
        }

        [UnityTest]
        public IEnumerator Older_receipt_does_not_replace_newer_receipt_or_deliver_twice()
        {
            using var fixture = new WireFixture { HoldMutations = true };
            using var entity = fixture.Open();
            var changes = new List<int>();
            entity.Changed += value => changes.Add(value.Number);
            var first = entity.UpdateWithOutcomeAsync(value => value.Number = 1);
            var second = entity.UpdateWithOutcomeAsync(value => value.Number = 2);
            fixture.ReplyMutation(fixture.Mutations[1], "accepted", 2, "2");
            yield return new UnityEngine.WaitUntil(() => second.IsCompleted);
            fixture.ReplyMutation(fixture.Mutations[0], "accepted", 1, "1");
            yield return new UnityEngine.WaitUntil(() => first.IsCompleted);
            Assert.That(entity.Value.Number, Is.EqualTo(2));
            Assert.That(changes, Is.EqualTo(new[] { 2 }));
        }

        [UnityTest]
        public IEnumerator Refresh_is_single_flight_and_cancellation_of_one_waiter_does_not_cancel_it()
        {
            using var fixture = new WireFixture { HoldRefreshes = true, FreshNumber = 1 };
            using var entity = fixture.Open();
            entity.UpdateAsync(value => value.Number++).GetAwaiter().GetResult();
            fixture.Push(0, "0");
            fixture.Push(0, "0");
            using var cancellation = new CancellationTokenSource();
            var cancelled = entity.UpdateWithOutcomeAsync(value => value.Number = 99, cancellation.Token);
            var next = entity.UpdateWithOutcomeAsync(value => value.Number++);
            Assert.That(fixture.Refreshes.Count, Is.EqualTo(1));
            cancellation.Cancel();
            yield return new UnityEngine.WaitUntil(() => cancelled.IsCompleted);
            Assert.That(cancelled.IsCanceled, Is.True);
            fixture.HoldRefreshes = false;
            fixture.Push(1, "1", (long)fixture.Refreshes[0]["RequestId"]);
            yield return new UnityEngine.WaitUntil(() => next.IsCompleted);
            Assert.That(next.Result.State, Is.EqualTo(PlayServDataMutationState.Accepted));
            Assert.That(fixture.Mutations.Count, Is.EqualTo(2));
            Assert.That(entity.Value.Number, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator Newer_receipt_during_refresh_requires_a_new_causal_read()
        {
            using var fixture = new WireFixture { HoldMutations = true, HoldRefreshes = true };
            using var entity = fixture.Open();
            var first = entity.UpdateWithOutcomeAsync(value => value.Number = 1);
            var second = entity.UpdateWithOutcomeAsync(value => value.Number = 2);
            fixture.ReplyMutation(fixture.Mutations[0], "accepted", 1, "1");
            yield return new UnityEngine.WaitUntil(() => first.IsCompleted);
            fixture.Push(0, "0");
            fixture.ReplyMutation(fixture.Mutations[1], "accepted", 2, "2");
            fixture.Push(1, "1", (long)fixture.Refreshes[0]["RequestId"]);
            yield return new UnityEngine.WaitUntil(() => second.IsCompleted);
            Assert.That(second.Result.State, Is.EqualTo(PlayServDataMutationState.Accepted));
            Assert.That(fixture.Refreshes.Count, Is.EqualTo(1), "No automatic refresh storm.");
            fixture.HoldRefreshes = fixture.HoldMutations = false;
            fixture.FreshNumber = 2;
            fixture.FreshRevision = "2";
            var third = entity.UpdateWithOutcomeAsync(value => value.Number++);
            yield return new UnityEngine.WaitUntil(() => third.IsCompleted);
            Assert.That((int)fixture.Mutations.Last()["Data"]["Number"], Is.EqualTo(3));
            Assert.That(fixture.Refreshes.Count, Is.EqualTo(2));
        }

        [Test]
        public void Cached_resolution_cannot_restore_pre_reconnect_state()
        {
            using var fixture = new WireFixture();
            using var entity = fixture.Open();
            var outcome = entity.UpdateWithOutcomeAsync(value => value.Number = 8).GetAwaiter().GetResult();
            fixture.Adapter.OnConnected();
            Assert.That(entity.Value.Number, Is.Zero);
            outcome.ResolveAsync().GetAwaiter().GetResult();
            Assert.That(entity.Value.Number, Is.Zero);
        }

        [UnityTest]
        public IEnumerator Closed_view_does_not_install_late_receipt_or_refresh()
        {
            using var fixture = new WireFixture { HoldMutations = true };
            var entity = fixture.Open();
            var task = entity.UpdateWithOutcomeAsync(value => value.Number = 7);
            entity.Dispose();
            fixture.ReplyMutation(fixture.Mutations[0], "accepted", 7, "1");
            yield return new UnityEngine.WaitUntil(() => task.IsCompleted);
            Assert.That(task.Result.State, Is.EqualTo(PlayServDataMutationState.Accepted));
            Assert.That(entity.Value.Number, Is.Zero);
            Assert.That(fixture.Refreshes, Is.Empty);
        }

        [Test]
        public void Fire_and_forget_mutation_cannot_bypass_an_unresolved_base()
        {
            using var fixture = new WireFixture { FailRefresh = true };
            using var entity = fixture.Open();
            entity.UpdateAsync(value => value.Number++).GetAwaiter().GetResult();
            fixture.Push(0, "0");
            var invoked = false;
            entity.Update(value => invoked = true);
            Assert.That(invoked, Is.False);
            Assert.That(fixture.Mutations.Count, Is.EqualTo(1));
        }

        [Test]
        public void Explicit_refresh_recovers_the_barrier_even_with_a_lagging_revision_label()
        {
            using var fixture = new WireFixture { FailRefresh = true, FreshNumber = 6, FreshRevision = "0" };
            using var entity = fixture.Open();
            entity.UpdateAsync(value => value.Number++).GetAwaiter().GetResult();
            fixture.Push(0, "0");
            fixture.FailRefresh = false;
            entity.RefreshAsync().GetAwaiter().GetResult();
            Assert.That(entity.Value.Number, Is.EqualTo(6));
            entity.UpdateAsync(value => value.Number++).GetAwaiter().GetResult();
            Assert.That(fixture.Refreshes.Count, Is.EqualTo(2));
        }

        [Test]
        public void Governed_patch_is_rejected_without_corrupting_accepted_array()
        {
            using var fixture = new WireFixture { FreshItems = new[] { 0, 1 } };
            using var entity = fixture.Open();
            var errors = new List<int>();
            entity.Error += error => errors.Add(error.ErrorCode);
            entity.UpdateAsync(value => value.Items.Add(1)).GetAwaiter().GetResult();
            fixture.AppendItemPatch();
            Assert.That(entity.Value.Items, Is.EqualTo(new[] { 0, 1 }));
            Assert.That(errors, Does.Contain(40001));
            Assert.That(fixture.Refreshes, Is.Empty);
        }

        [UnityTest]
        public IEnumerator Mutation_from_refreshed_changed_callback_uses_the_installed_fresh_state()
        {
            using var fixture = new WireFixture();
            using var entity = fixture.Open();
            entity.UpdateAsync(value => value.Number++).GetAwaiter().GetResult();
            Task<PlayServDataMutationOutcome> nested = null;
            entity.Changed += value => { if (nested == null && value.Number == 1) nested = entity.UpdateWithOutcomeAsync(next => next.Number = 2); };
            fixture.Push(0, "0");
            yield return new UnityEngine.WaitUntil(() => nested != null && nested.IsCompleted);
            Assert.That(entity.Value.Number, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator Timeout_of_ambiguous_read_preserves_accepted_outcome_without_retrying_write()
        {
            using var fixture = new WireFixture { HoldRefreshes = true };
            using var entity = fixture.Open();
            fixture.BeforeMutationResponse = () => fixture.Push(4, "0");
            var mutation = entity.UpdateWithOutcomeAsync(value => value.Number++);
            var deadline = UnityEngine.Time.realtimeSinceStartup + 11;
            while (!mutation.IsCompleted && UnityEngine.Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(mutation.IsCompleted, Is.True);
            Assert.That(mutation.Result.State, Is.EqualTo(PlayServDataMutationState.Accepted));
            Assert.That(fixture.Refreshes.Count, Is.EqualTo(1));
            Assert.That(fixture.Mutations.Count, Is.EqualTo(1));
            fixture.HoldRefreshes = false;
            fixture.FailRefresh = true;
            Assert.Throws<DataSubscriptionException>(() => entity.UpdateAsync(value => value.Number++).GetAwaiter().GetResult());
            Assert.That(fixture.Mutations.Count, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator Closing_during_refresh_suppresses_late_data_and_callbacks()
        {
            using var fixture = new WireFixture { HoldRefreshes = true };
            var entity = fixture.Open();
            var changes = 0;
            entity.Changed += value => changes++;
            entity.UpdateAsync(value => value.Number++).GetAwaiter().GetResult();
            fixture.Push(0, "0");
            entity.Dispose();
            fixture.Push(77, "1", (long)fixture.Refreshes[0]["RequestId"]);
            yield return null;
            Assert.That(entity.Value.Number, Is.EqualTo(1));
            Assert.That(changes, Is.EqualTo(1));
        }

        [Test]
        public void Failed_projector_on_correlated_stream_refresh_keeps_the_mutation_barrier()
        {
            using var fixture = new WireFixture { AcceptedNumber = 5, FreshNumber = 5 };
            using var entity = fixture.OpenProjected(false);
            Assert.That(entity.UpdateWithOutcomeAsync(value => value.Number = 1).GetAwaiter().GetResult().State,
                Is.EqualTo(PlayServDataMutationState.Accepted));
            var invoked = false;
            LogAssert.Expect(UnityEngine.LogType.Error, "[PlayServ][data] Unexpected error processing transport update: InvalidOperationException");
            Assert.Throws<DataSubscriptionException>(() => entity.UpdateAsync(value => invoked = true).GetAwaiter().GetResult());
            Assert.That(invoked, Is.False);
            Assert.That(entity.Value.Number, Is.Zero);
            Assert.That(fixture.Mutations.Count, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Invalid_replacement_baseline_cannot_clear_the_mutation_barrier(bool reconnect)
        {
            using var fixture = new WireFixture { AcceptedNumber = 5, FreshNumber = 5 };
            using var entity = fixture.OpenProjected(false);
            Assert.That(entity.UpdateWithOutcomeAsync(value => value.Number = 1).GetAwaiter().GetResult().State,
                Is.EqualTo(PlayServDataMutationState.Accepted));
            LogAssert.Expect(UnityEngine.LogType.Error, "[PlayServ][data] Unexpected error processing transport update: InvalidOperationException");
            if (reconnect)
            {
                fixture.InitialNumber = 5;
                fixture.Adapter.OnConnected();
            }
            else
            {
                fixture.Epoch = "epoch-b";
                fixture.Push(5, "0");
            }
            Assert.That(entity.State, Is.EqualTo(PlayServSubscriptionState.Active));
            fixture.FailRefresh = true;
            var invoked = false;
            Assert.Throws<DataSubscriptionException>(() => entity.UpdateAsync(value => invoked = true).GetAwaiter().GetResult());
            Assert.That(invoked, Is.False);
            Assert.That(entity.Value.Number, Is.Zero);
            Assert.That(fixture.Mutations.Count, Is.EqualTo(1));
            fixture.FailRefresh = false;
            fixture.FreshNumber = 2;
            entity.RefreshAsync().GetAwaiter().GetResult();
            Assert.That(entity.Value.Number, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator Failed_projector_on_normal_poll_keeps_the_mutation_barrier()
        {
            using var fixture = new WireFixture { AcceptedNumber = 5, FreshNumber = 5 };
            using var entity = fixture.OpenProjected(true);
            entity.UpdateWithOutcomeAsync(value => value.Number = 1).GetAwaiter().GetResult();
            LogAssert.Expect(UnityEngine.LogType.Error, "[PlayServ][data] Failed to map polling payload: InvalidOperationException");
            var deadline = UnityEngine.Time.realtimeSinceStartup + 2;
            while (fixture.Gets.Count == 0 && UnityEngine.Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(fixture.Gets.Count, Is.GreaterThan(0));
            var invoked = false;
            Assert.Throws<DataSubscriptionException>(() => entity.UpdateAsync(value => invoked = true).GetAwaiter().GetResult());
            Assert.That(invoked, Is.False);
            Assert.That(fixture.Mutations.Count, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator Explicit_async_recovery_from_worker_delivers_on_captured_context() => RecoveryFromWorker(true);

        [UnityTest]
        public IEnumerator Explicit_void_recovery_from_worker_delivers_on_captured_context() => RecoveryFromWorker(false);

        private static IEnumerator RecoveryFromWorker(bool awaitable)
        {
            var saved = SynchronizationContext.Current;
            var context = new PumpContext();
            WireFixture fixture;
            ISharedEntity<StrictCounter> entity;
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                fixture = new WireFixture { AcceptedNumber = 5, FreshNumber = 1 };
                entity = fixture.OpenProjected(true);
                entity.UpdateWithOutcomeAsync(value => value.Number = 1).GetAwaiter().GetResult();
            }
            finally { SynchronizationContext.SetSynchronizationContext(saved); }
            using (fixture)
            using (entity)
            {
                bool? onContext = null;
                entity.Changed += value => onContext = ReferenceEquals(SynchronizationContext.Current, context);
                var work = Task.Run(async () => { if (awaitable) await entity.RefreshAsync(); else entity.Refresh(); });
                var deadline = UnityEngine.Time.realtimeSinceStartup + 3;
                while ((!work.IsCompleted || !onContext.HasValue) && UnityEngine.Time.realtimeSinceStartup < deadline)
                {
                    context.Drain();
                    yield return null;
                }
                Assert.That(work.IsCompleted, Is.True);
                Assert.That(work.Exception, Is.Null);
                Assert.That(onContext, Is.True);
            }
        }

        [UnityTest]
        public IEnumerator Held_transport_refresh_send_is_bounded_and_preserves_acceptance()
        {
            using var fixture = new WireFixture { HoldRefreshSend = true };
            using var entity = fixture.Open();
            fixture.BeforeMutationResponse = () => fixture.Push(4, "0");
            var task = entity.UpdateWithOutcomeAsync(value => value.Number++);
            var deadline = UnityEngine.Time.realtimeSinceStartup + 10;
            while (!task.IsCompleted && UnityEngine.Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(task.IsCompleted, Is.True);
            Assert.That(task.Result.State, Is.EqualTo(PlayServDataMutationState.Accepted));
            Assert.That(fixture.Mutations.Count, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator Closing_during_held_refresh_send_finishes_local_operation()
        {
            using var fixture = new WireFixture { HoldRefreshSend = true };
            var entity = fixture.Open();
            fixture.BeforeMutationResponse = () => fixture.Push(4, "0");
            var task = entity.UpdateWithOutcomeAsync(value => value.Number++);
            entity.Dispose();
            var deadline = UnityEngine.Time.realtimeSinceStartup + 2;
            while (!task.IsCompleted && UnityEngine.Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(task.IsCompleted, Is.True);
            Assert.That(task.Result.State, Is.EqualTo(PlayServDataMutationState.Accepted));
        }

        [UnityTest]
        public IEnumerator Held_polling_recovery_send_is_bounded()
        {
            using var fixture = new WireFixture { AcceptedNumber = 5, HoldGetSend = true };
            using var entity = fixture.OpenProjected(true);
            entity.UpdateWithOutcomeAsync(value => value.Number = 1).GetAwaiter().GetResult();
            var invoked = false;
            var next = entity.UpdateWithOutcomeAsync(value => invoked = true);
            var deadline = UnityEngine.Time.realtimeSinceStartup + 6;
            while (!next.IsCompleted && UnityEngine.Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(next.IsCompleted, Is.True);
            Assert.That(next.Exception?.GetBaseException(), Is.InstanceOf<DataSubscriptionException>());
            Assert.That(invoked, Is.False);
            Assert.That(fixture.Mutations.Count, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator Timed_out_polling_never_queues_more_physical_sends_behind_a_stalled_send()
        {
            using var fixture = new WireFixture { HoldGetSend = true };
            var client = new DataGetClient(fixture, new CaptureLogger(), new DataSubscriptionRequestIdSource(), new NewtonsoftJsonCodec());
            var polling = new DataSubscriptionPollingCoordinator(fixture, client, new CaptureLogger());
            var handle = polling.StartDataByKeyPolling("one", "{ Counters { Number } }",
                new Dictionary<string, object>(), 30, 60, _ => { });
            var deadline = UnityEngine.Time.realtimeSinceStartup + 0.35f;
            while (UnityEngine.Time.realtimeSinceStartup < deadline) yield return null;
            handle.Dispose();
            Assert.That(fixture.DataGetSendAttempts, Is.EqualTo(1));
            fixture.ReleaseSend();
            yield return null;
            Assert.That(fixture.Gets.Count, Is.EqualTo(1), "No abandoned polling sends are flushed later.");
        }

        [Serializable]
        private sealed class Counter { public int Number; public List<int> Items = new List<int>(); public Related Relation = new Related(); }
        [Serializable]
        private sealed class Related { public int Value; }

        private sealed class StrictCounter
        {
            private int _number;
            public int Number { get => _number; set { if (value == 5) throw new InvalidOperationException("private-projection-input"); _number = value; } }
        }

        private sealed class PumpContext : SynchronizationContext
        {
            private readonly Queue<Action> _work = new Queue<Action>();
            public override void Post(SendOrPostCallback callback, object state) { lock (_work) _work.Enqueue(() => callback(state)); }
            public void Drain()
            {
                var saved = Current;
                SetSynchronizationContext(this);
                try
                {
                    while (true)
                    {
                        Action next;
                        lock (_work) { if (_work.Count == 0) return; next = _work.Dequeue(); }
                        next();
                    }
                }
                finally { SetSynchronizationContext(saved); }
            }
        }

        private sealed class WireFixture : IPlayServCommandBus, IPlayServRuntimeIdentity, ITransportImplementation, IDisposable
        {
            private readonly Observable _incoming = new Observable();
            private readonly NewtonsoftJsonCodec _json = new NewtonsoftJsonCodec();
            private readonly Transport _transport;
            public readonly List<Newtonsoft.Json.Linq.JObject> Mutations = new List<Newtonsoft.Json.Linq.JObject>();
            public readonly List<Newtonsoft.Json.Linq.JObject> Refreshes = new List<Newtonsoft.Json.Linq.JObject>();
            public readonly List<Newtonsoft.Json.Linq.JObject> Gets = new List<Newtonsoft.Json.Linq.JObject>();
            public readonly List<Newtonsoft.Json.Linq.JObject> Statuses = new List<Newtonsoft.Json.Linq.JObject>();
            public int FreshNumber = 1;
            public int InitialNumber;
            public string Epoch = "epoch-a";
            public int[] FreshItems = { 0 };
            public string FreshRevision = "1";
            public bool FailRefresh;
            public bool HoldDataGet;
            public bool NullGet;
            public bool HoldMutations;
            public bool HoldRefreshes;
            public bool HoldRefreshSend;
            public bool HoldGetSend;
            public int? AcceptedNumber;
            public bool RelationOnly;
            public int DataGetSendAttempts;
            private readonly TaskCompletionSource<bool> _sendGate = new TaskCompletionSource<bool>();
            public string MutationState = "accepted";
            public Action BeforeMutationResponse;
            private long _sequence = 1;
            public PlayServDataSubscriptionAdapter Adapter { get; }
            public string PlayerId => "player-a";
            public PlayServState State => PlayServState.Online;

            public WireFixture()
            {
                CommandTypeProviderRegistry.Register(new DataSubscriptionCommandTypeProvider());
                _transport = new Transport(this, new Playserv.Proxy.Implementation.JsonSerializer(_json,
                    new NewtonsoftCommandPayloadMapper()), new RequestIdGenerator(), new CaptureLogger());
                Adapter = new PlayServDataSubscriptionAdapter(this, new CaptureLogger(), _json);
            }

            public ISharedEntity<Counter> Open() => new SharedEntityBuilder<Counter>(Adapter, "Counters").Key("one")
                .UseTransport().BindAsync().GetAwaiter().GetResult();
            public ISharedEntity<Counter> OpenPolling() => new SharedEntity<Counter>(Adapter, Adapter.NextSubscriptionId(), "one", "Counters", null,
                "{ Counters { Number } }", new Dictionary<string, object>());
            public ISharedEntity<StrictCounter> OpenProjected(bool polling)
            {
                var builder = new SharedEntityBuilder<Counter>(Adapter, "Counters").Key("one");
                return (polling ? builder.UsePolling() : builder.UseTransport())
                    .Select(value => new StrictCounter { Number = value.Number }).BindAsync().GetAwaiter().GetResult();
            }

            public void Push(int number, string revision, long requestId = 0) => Emit("DataSubscriptionUpdate", new
            {
                RequestId = requestId, DataSubscriptionId = 10, UpdateType = "Overwrite", IsCollection = false,
                EntityType = "Counters", IdValue = "one", Data = new { Number = number, Items = FreshItems },
                Authority = new { Epoch, Sequence = (++_sequence).ToString(), Revision = revision, Complete = true }
            });

            public void AppendItemPatch() => Emit("DataSubscriptionUpdate", new
            {
                RequestId = 0, DataSubscriptionId = 10, UpdateType = "Patch", IsCollection = false,
                Data = new[] { new { op = "add", path = "/Items/1", value = 1 } },
                Authority = new { Epoch, Sequence = (++_sequence).ToString(), Revision = "1", Complete = false }
            });

            public void ReplyGet(Newtonsoft.Json.Linq.JObject request, int number)
            {
                object data = RelationOnly
                    ? new { Number = 0, updated_at = "2026-10-22T10:00:00.000Z", Relation = new { Value = number } }
                    : (object)new { Number = number };
                Emit("DataGetResponse", new { RequestId = (long)request["RequestId"], Result = new { Data = new { Counters = data } } });
            }

            public void ReplyMutation(Newtonsoft.Json.Linq.JObject request, string state, int number, string revision)
            {
                object data = RelationOnly
                    ? new { Number = 0, updated_at = "2026-10-22T10:00:00.000Z", Relation = new { Value = (int)request["Data"]["Relation"]["Value"] } }
                    : (object)new { Number = number, Items = request["Data"]["Items"] };
                Emit("DataMutationResponse", new { RequestId = (long)request["RequestId"], Result = new { Success = state == "accepted",
                    Authority = new { OperationId = (string)request["Authority"]["OperationId"], Epoch, Outcome = state,
                        ErrorCode = state == "rejected" ? (int?)32105 : null, Revision = revision,
                        Data = data } } });
            }

            public IDisposable On<T>(Action<T> onNext) => _transport.OnReceive<T>().Subscribe(new Observer<T>(onNext));
            public IDisposable OnCommand(string name, Action<object> onNext) => _transport.OnReceive(name).Subscribe(new Observer<object>(onNext));
            public Task SendAsync<T>(T command, string moduleName = null)
            {
                if (command is Playserv.DataSubscription.Requests.DataGetRequest) DataGetSendAttempts++;
                return _transport.Send(command, moduleName);
            }
            public void ReleaseSend() => _sendGate.TrySetResult(true);
            public Task<bool> Connect() => Task.FromResult(true);
            public void ResetConnection() { }
            public IObservable<byte[]> OnReceive() => _incoming;
            public Task Send(byte[] bytes)
            {
                var frame = Newtonsoft.Json.Linq.JObject.Parse(Encoding.UTF8.GetString(bytes));
                var command = ((string)frame["Command"]).Split('.').Last();
                var payload = (Newtonsoft.Json.Linq.JObject)frame["Payload"];
                var id = (long)payload["RequestId"];
                if (command == "DataAuthorityContextRequest")
                    Emit("DataAuthorityContextResponse", new { RequestId = id, Result = new { Capability = "authoritative_state_v1", Epoch, RetrySession = "session-a", RetryWindowMs = 120000 } });
                if (command == "DataSubscriptionRequest")
                {
                    Emit("DataSubscriptionResponse", new { RequestId = id, Result = new { SubscriptionId = 10, Authority = new { Epoch } } });
                    Emit("DataSubscriptionUpdate", new { RequestId = id, DataSubscriptionId = 10, UpdateType = "Overwrite", IsCollection = false,
                        EntityType = "Counters", IdValue = "one", Data = new { Number = InitialNumber, Items = new[] { 0 } },
                        Authority = new { Epoch, Sequence = "1", Revision = "0", Complete = true } });
                }
                if (command == "DataMutationRequest")
                {
                    Mutations.Add(payload);
                    BeforeMutationResponse?.Invoke();
                    if (!HoldMutations) ReplyMutation(payload, MutationState, AcceptedNumber ?? (int)payload["Data"]["Number"], Mutations.Count.ToString());
                }
                if (command == "DataAuthorityStatusRequest")
                {
                    Statuses.Add(payload);
                    Emit("DataAuthorityStatusResponse", new { RequestId = id, Result = new { Known = true,
                        Authority = new { OperationId = (string)payload["OperationId"], Epoch, Outcome = "accepted",
                            Revision = "1", Data = new { Number = FreshNumber } } } });
                }
                if (command == "DataSubscriptionRefreshRequest")
                {
                    Refreshes.Add(payload);
                    if (HoldRefreshSend) return _sendGate.Task;
                    if (HoldRefreshes) return Task.CompletedTask;
                    if (FailRefresh) Emit("DataSubscriptionUpdate", new { RequestId = id, DataSubscriptionId = 10, ErrorCode = 500, ErrorMessage = "secret-refresh-payload" });
                    else Push(FreshNumber, FreshRevision, id);
                }
                if (command == "DataGetRequest")
                {
                    Gets.Add(payload);
                    if (HoldGetSend) return _sendGate.Task;
                    if (!HoldDataGet)
                    {
                        if (NullGet) Emit("DataGetResponse", new { RequestId = id, Result = new { Data = new { Counters = (object)null } } });
                        else ReplyGet(payload, FreshNumber);
                    }
                }
                if (command == "DataSubscriptionCloseRequest")
                    Emit("DataSubscriptionCloseResponse", new { RequestId = id, Result = new { Success = true } });
                return Task.CompletedTask;
            }

            private void Emit(string command, object payload) => _incoming.Emit(Encoding.UTF8.GetBytes(_json.Serialize(new { Command = "module_dataflow." + command, Payload = payload })));
            public void Dispose() { Adapter.Dispose(); _transport.Dispose(); _sendGate.TrySetResult(true); }
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
            public void Log(string message) { }
            public void LogWarning(string message) { }
            public void LogError(string message) { }
        }
    }
}
