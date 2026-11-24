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
        [UnityTest] public IEnumerator Queue_folds_only_pending_reloads_and_completes_every_caller() => QueueRun(async () =>
        {
            var f = new QueueRecord(); var gate = f.Block();
            var active = f.Record.ReloadAsync();
            var pending = Enumerable.Range(0, 5).Select(_ => f.Record.ReloadAsync()).ToArray();
            Assert.That(f.Http.Requests.Count, Is.EqualTo(1));
            gate.SetResult(QueueResponse());
            await Task.WhenAll(pending.Concat(new[] { active }));
            Assert.That(f.Http.Requests.Count, Is.EqualTo(2), "One active GET and one folded pending GET.");
        });

        [UnityTest] public IEnumerator Queue_folded_saves_leave_edits_during_their_shared_request_dirty() => QueueRun(async () =>
        {
            var f = new QueueRecord(); var firstGate = f.Block(); var secondGate = f.Block();
            f.Record.Value.DisplayName = "first"; var first = f.Record.SaveAsync();
            f.Record.Value.DisplayName = "second";
            var second = f.Record.SaveAsync(); var folded = f.Record.SaveAsync();
            firstGate.SetResult(QueueResponse("first", "v2"));
            await QueueUntil(() => f.Http.Requests.Count == 2);
            f.Record.Value.DisplayName = "third";
            secondGate.SetResult(QueueResponse("second", "v3"));
            await Task.WhenAll(first, second, folded);
            Assert.That(f.Http.Requests.Count, Is.EqualTo(2));
            Assert.That(f.Http.Requests[1].IfMatch, Is.EqualTo("\"v2\""));
            Assert.That(f.Http.Requests[1].JsonBody, Is.EqualTo("{\"display_name\":\"second\"}"));
            Assert.That(f.Record.Value.DisplayName, Is.EqualTo("third"));
            Assert.That(f.Record.HasPendingChanges, Is.True);
            f.Replies.Enqueue(() => Task.FromResult(QueueResponse("third", "v4")));
            await f.Record.SaveAsync();
            Assert.That(f.Http.Requests.Last().IfMatch, Is.EqualTo("\"v3\""));
            Assert.That(f.Record.HasPendingChanges, Is.False);
        });

        [UnityTest] public IEnumerator Queue_does_not_fold_across_reload_or_realtime_boundaries() => QueueRun(async () =>
        {
            var f = new QueueRecord(); var gate = f.Block();
            var active = f.Record.ReloadAsync();
            f.Record.Value.DisplayName = "edit";
            var save1 = f.Record.SaveAsync(); var reload = f.Record.ReloadAsync(); var save2 = f.Record.SaveAsync();
            gate.SetException(HttpError(503, "unavailable", "{}"));
            await QueueFailure<PlayServDataException>(active);
            await Task.WhenAll(save1, reload, save2);
            Assert.That(f.Http.Requests.Select(r => r.Method), Is.EqualTo(new[] { "GET", "PATCH", "GET" }));
            // The last clean save is still an operation, even though it needs no PATCH.
            var next = f.Block(); var a = f.Record.ReloadAsync();
            var b = f.Record.ReloadAsync(); var realtime = f.Record.SynchronizeRealtimeAsync(CancellationToken.None);
            var c = f.Record.ReloadAsync();
            next.SetResult(QueueResponse()); await Task.WhenAll(a, b, realtime, c);
            Assert.That(f.Http.Requests.Count, Is.EqualTo(7));
        });

        [UnityTest] public IEnumerator Queue_cancelling_one_folded_caller_preserves_the_other_callers() => QueueRun(async () =>
        {
            var f = new QueueRecord(); var gate = f.Block(); var active = f.Record.ReloadAsync();
            using var cancel = new CancellationTokenSource();
            var cancelled = f.Record.ReloadAsync(cancel.Token);
            var kept1 = f.Record.ReloadAsync(); var kept2 = f.Record.ReloadAsync();
            cancel.Cancel(); await QueueFailure<OperationCanceledException>(cancelled);
            gate.SetResult(QueueResponse()); await Task.WhenAll(active, kept1, kept2);
            Assert.That(f.Http.Requests.Count, Is.EqualTo(2));
        });

        [UnityTest] public IEnumerator Queue_active_cancellation_stops_waiting_but_keeps_the_wire_slot_and_applies_ack() => QueueRun(async () =>
        {
            var f = new QueueRecord(); var gate = f.Block(); var reloadGate = f.Block();
            using var cancel = new CancellationTokenSource();
            f.Record.Value.DisplayName = "sent";
            var save = f.Record.SaveAsync(cancel.Token); var reload = f.Record.ReloadAsync();
            cancel.Cancel();
            try
            {
                await QueueUntil(() => save.IsCompleted);
                await QueueFailure<OperationCanceledException>(save);
                Assert.That(f.Http.Requests.Count, Is.EqualTo(1));
                Assert.That(f.Tokens.Single().IsCancellationRequested, Is.False);
            }
            finally { gate.TrySetResult(QueueResponse("sent", "v2")); }
            await QueueUntil(() => f.Http.Requests.Count == 2);
            Assert.That(f.Record.Value.DisplayName, Is.EqualTo("sent"));
            Assert.That(f.Record.ETag, Is.EqualTo("\"v2\""));
            Assert.That(f.Record.HasPendingChanges, Is.False);
            reloadGate.SetResult(QueueResponse());
            await reload;
            Assert.That(f.Http.Requests.Count, Is.EqualTo(2));
        });

        [UnityTest] public IEnumerator Queue_all_pending_callers_cancel_without_io_or_cross_kind_folding() => QueueRun(async () =>
        {
            var f = new QueueRecord(); var gate = f.Block(); var active = f.Record.ReloadAsync();
            var before = f.Record.ReloadAsync();
            using var cancel = new CancellationTokenSource();
            var skipped = f.Record.SaveAsync(cancel.Token); cancel.Cancel();
            var after = f.Record.ReloadAsync();
            await QueueFailure<OperationCanceledException>(skipped);
            gate.SetResult(QueueResponse()); await Task.WhenAll(active, before, after);
            Assert.That(f.Http.Requests.Count, Is.EqualTo(3));
            await QueueFailure<OperationCanceledException>(f.Record.DeleteAsync(cancel.Token));
            Assert.That(f.Record.IsDeleted, Is.False);
        });

        [UnityTest] public IEnumerator Queue_folded_errors_complete_every_caller_and_next_kind_runs() => QueueRun(async () =>
        {
            var f = new QueueRecord(); var gate = f.Block(); var active = f.Record.ReloadAsync();
            f.Replies.Enqueue(() => throw HttpError(412, "precondition_failed", "{}"));
            var one = f.Record.ReloadAsync(); var two = f.Record.ReloadAsync(); var three = f.Record.ReloadAsync();
            var delete = f.Record.DeleteAsync(); gate.SetResult(QueueResponse()); await active;
            await QueueFailure<PlayServDataException>(one); await QueueFailure<PlayServDataException>(two); await QueueFailure<PlayServDataException>(three);
            await delete;
            Assert.That(f.Record.IsDeleted, Is.True);
            Assert.That(f.Http.Requests.Select(r => r.Method), Is.EqualTo(new[] { "GET", "GET", "DELETE" }));
        });

        [UnityTest] public IEnumerator Queue_folded_deletes_succeed_together_and_drain_later_work() => QueueRun(async () =>
        {
            var f = new QueueRecord(); var gate = f.Block(); var active = f.Record.ReloadAsync();
            var delete1 = f.Record.DeleteAsync(); var delete2 = f.Record.DeleteAsync();
            var save = f.Record.SaveAsync(); var reload = f.Record.ReloadAsync();
            gate.SetResult(QueueResponse()); await Task.WhenAll(active, delete1, delete2);
            await QueueFailure<InvalidOperationException>(save); await QueueFailure<InvalidOperationException>(reload);
            await QueueFailure<InvalidOperationException>(f.Record.DeleteAsync());
            Assert.That(f.Http.Requests.Select(r => r.Method), Is.EqualTo(new[] { "GET", "DELETE" }));
        });

        [UnityTest] public IEnumerator Queue_active_delete_is_not_folded_and_failed_delete_does_not_poison_queue() => QueueRun(async () =>
        {
            var f = new QueueRecord(); var gate = f.Block(); var first = f.Record.DeleteAsync(); var second = f.Record.DeleteAsync();
            gate.SetException(HttpError(412, "precondition_failed", "{}"));
            await QueueFailure<PlayServDataException>(first); await second;
            Assert.That(f.Http.Requests.Count, Is.EqualTo(2)); Assert.That(f.Record.IsDeleted, Is.True);
        });

        [UnityTest] public IEnumerator Queue_realtime_delete_drains_public_work_without_an_http_delete() => QueueRun(async () =>
        {
            var f = new QueueRecord(); var gate = f.Block(); var active = f.Record.ReloadAsync();
            var removed = f.Record.MarkDeletedFromRealtimeAsync(CancellationToken.None);
            var save = f.Record.SaveAsync(); var reload = f.Record.ReloadAsync();
            gate.SetResult(QueueResponse()); await active; Assert.That(await removed, Is.True);
            await QueueFailure<InvalidOperationException>(save); await QueueFailure<InvalidOperationException>(reload);
            Assert.That(f.Http.Requests.Count, Is.EqualTo(1));
            Assert.That(await f.Record.MarkDeletedFromRealtimeAsync(CancellationToken.None), Is.False);
        });

        [UnityTest] public IEnumerator Queue_handles_with_equal_ids_are_independent() => QueueRun(async () =>
        {
            var one = new QueueRecord(); var two = new QueueRecord(); var gate = one.Block();
            var pending = one.Record.ReloadAsync(); await two.Record.ReloadAsync();
            Assert.That(pending.IsCompleted, Is.False); Assert.That(two.Http.Requests.Count, Is.EqualTo(1));
            gate.SetResult(QueueResponse()); await pending;
        });

        [UnityTest] public IEnumerator Queue_bulk_calls_use_the_handle_queue_and_clean_saves_are_synchronous() => QueueRun(async () =>
        {
            var f = new QueueRecord();
            for (var i = 0; i < 100; i++) Assert.That(f.Record.SaveAsync().IsCompleted, Is.True);
            var gate = f.Block(); var active = f.Record.ReloadAsync();
            var delete1 = f.Record.OwnerSet.BulkDeleteAsync(new[] { f.Record });
            var delete2 = f.Record.OwnerSet.BulkDeleteAsync(new[] { f.Record });
            gate.SetResult(QueueResponse()); await Task.WhenAll(active, delete1, delete2);
            Assert.That(delete1.Result.IsSuccess, Is.True); Assert.That(delete2.Result.IsSuccess, Is.True);
            Assert.That(f.Http.Requests.Count, Is.EqualTo(2));
        });

        [UnityTest] public IEnumerator Queue_save_reload_save_preserves_failure_and_success_boundaries() => QueueRun(async () =>
        {
            var f = new QueueRecord(); var gate = f.Block(); var active = f.Record.ReloadAsync();
            f.Record.Value = null;
            var firstSave = f.Record.SaveAsync(); var reload = f.Record.ReloadAsync(); var lastSave = f.Record.SaveAsync();
            gate.SetException(HttpError(503, "unavailable", "{}"));
            await QueueFailure<PlayServDataException>(active);
            await QueueFailure<InvalidOperationException>(firstSave);
            await Task.WhenAll(reload, lastSave);
            Assert.That(f.Http.Requests.Select(r => r.Method), Is.EqualTo(new[] { "GET", "GET" }));
            Assert.That(f.Record.Value, Is.Not.Null);
        });

        [UnityTest] public IEnumerator Queue_all_folded_callers_cancel_before_dispatch_without_a_request() => QueueRun(async () =>
        {
            var f = new QueueRecord(); var gate = f.Block(); var active = f.Record.ReloadAsync();
            using var first = new CancellationTokenSource(); using var second = new CancellationTokenSource();
            var one = f.Record.ReloadAsync(first.Token); var two = f.Record.ReloadAsync(second.Token);
            first.Cancel(); second.Cancel();
            await QueueFailure<OperationCanceledException>(one); await QueueFailure<OperationCanceledException>(two);
            gate.SetResult(QueueResponse()); await active;
            Assert.That(f.Http.Requests.Count, Is.EqualTo(1));
        });

        [UnityTest] public IEnumerator Queue_cancelled_active_delete_still_marks_terminal_after_success() => QueueRun(async () =>
        {
            var f = new QueueRecord(); var gate = f.Block(); using var cancel = new CancellationTokenSource();
            var delete = f.Record.DeleteAsync(cancel.Token); var reload = f.Record.ReloadAsync();
            cancel.Cancel(); await QueueFailure<OperationCanceledException>(delete);
            Assert.That(f.Record.IsDeleted, Is.False);
            gate.SetResult(QueueResponse()); await QueueFailure<InvalidOperationException>(reload);
            Assert.That(f.Record.IsDeleted, Is.True); Assert.That(f.Http.Requests.Count, Is.EqualTo(1));
        });

        [UnityTest] public IEnumerator Queue_cancelled_realtime_response_cannot_overwrite_local_edits() => QueueRun(async () =>
        {
            var f = new QueueRecord(); var gate = f.Block(); using var cancel = new CancellationTokenSource();
            var refresh = f.Record.SynchronizeRealtimeAsync(cancel.Token);
            cancel.Cancel(); await QueueFailure<OperationCanceledException>(refresh);
            f.Record.Value.DisplayName = "new local edit";
            var save = f.Record.SaveAsync();
            gate.SetResult(QueueResponse("late remote", "v9")); await save;
            Assert.That(f.Http.Requests.Count, Is.EqualTo(2), "A cancelled internal refresh must not erase the edit before the next save.");
            Assert.That(f.Http.Requests.Last().JsonBody, Is.EqualTo("{\"display_name\":\"new local edit\"}"));
            Assert.That(f.Http.Requests.Last().IfMatch, Is.EqualTo("\"v1\""));
        });

        [UnityTest] public IEnumerator Queue_worker_call_runs_http_on_the_handle_context() => QueueRun(async () =>
        {
            var f = new QueueRecord(); var owner = Thread.CurrentThread.ManagedThreadId; var executed = 0;
            f.Http.DataHandler = (_, __) => { executed = Thread.CurrentThread.ManagedThreadId; return Task.FromResult(QueueResponse()); };
            await Task.Run(() => f.Record.ReloadAsync());
            Assert.That(executed, Is.EqualTo(owner));
        });

        [UnityTest] public IEnumerator Queue_reentrant_operation_appends_without_executing_inside_the_current_http_call() => QueueRun(async () =>
        {
            var f = new QueueRecord(); Task nested = null; var calls = 0; var inside = false;
            f.Http.DataHandler = (_, __) =>
            {
                Assert.That(inside, Is.False, "The record operation runner reentered.");
                inside = true;
                if (++calls == 1) nested = f.Record.ReloadAsync();
                inside = false; return Task.FromResult(QueueResponse());
            };
            await f.Record.ReloadAsync(); await nested;
            Assert.That(calls, Is.EqualTo(2));
        });

        private sealed class QueueRecord
        {
            internal readonly FakeDataHttpClient Http = new FakeDataHttpClient();
            internal readonly Queue<Func<Task<PlayServRuntimeDataResponse>>> Replies = new Queue<Func<Task<PlayServRuntimeDataResponse>>>();
            internal readonly List<CancellationToken> Tokens = new List<CancellationToken>();
            internal readonly PlayServRecord<InventoryItem> Record;
            internal QueueRecord()
            {
                Http.EnqueueJson(200, RecordJson("rec_1", "sword", "old"), "\"v1\"");
                Record = CreateSet<InventoryItem>(Http, "ent_inventory").LoadAsync("rec_1").GetAwaiter().GetResult();
                Http.Requests.Clear();
                Http.DataHandler = (_, ct) => { Tokens.Add(ct); return Replies.Count > 0 ? Replies.Dequeue()() : Task.FromResult(QueueResponse()); };
            }
            internal TaskCompletionSource<PlayServRuntimeDataResponse> Block()
            {
                var response = new TaskCompletionSource<PlayServRuntimeDataResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
                Replies.Enqueue(() => response.Task); return response;
            }
        }
        private static PlayServRuntimeDataResponse QueueResponse(string name = "old", string etag = "v2") =>
            new PlayServRuntimeDataResponse(200, RecordJson("rec_1", "sword", name), "\"" + etag + "\"", null);
        private static IEnumerator QueueRun(Func<Task> run)
        { var task = run(); yield return WaitForSaveTask(task); task.GetAwaiter().GetResult(); }
        private static async Task QueueUntil(Func<bool> done)
        {
            var until = DateTime.UtcNow.AddSeconds(2);
            while (!done() && DateTime.UtcNow < until) await Task.Yield();
            Assert.That(done(), Is.True, "Queued operation did not make progress.");
        }
        private static async Task QueueFailure<TException>(Task task) where TException : Exception
        {
            try { await task; Assert.Fail("Expected " + typeof(TException).Name); }
            catch (TException) { }
        }
    }
}
