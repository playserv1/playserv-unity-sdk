using System;
using System.Collections.Generic;
using NUnit.Framework;
using Playserv.Prediction;
using UnityEngine;

namespace Playserv.Tests.Runtime
{
    public sealed class PlayServPredictionTests
    {
        private static PlayServPrediction<float, float> Create(int capacity = 128,
            Func<float, float, PredictionSimulationContext, float> simulate = null)
        {
            var p = new PlayServPrediction<float, float>(simulate ?? ((s, i, c) => s + i * c.DeltaTime),
                s => s, i => i, capacity);
            p.Reset(0, 0);
            return p;
        }

        [Test]
        public void Local_input_moves_before_any_server_response()
        {
            var p = Create();
            Assert.That(p.TryPredict(10, .02f, out var command), Is.True);
            Assert.That(p.State, Is.EqualTo(.2f).Within(.00001));
            Assert.That(command.Sequence, Is.EqualTo(1));
            Assert.That(command.Generation, Is.EqualTo(p.Generation));
            Assert.That(command.Input, Is.EqualTo(10));
            Assert.That(command.DeltaTime, Is.EqualTo(.02f));
            Assert.That(p.LastAcknowledgedSequence, Is.Zero);
        }

        [Test]
        public void Correction_replays_only_inputs_after_the_applied_watermark()
        {
            var calls = new List<PredictionSimulationContext>();
            var p = Create(simulate: (s, i, c) => { calls.Add(c); return s + i * c.DeltaTime; });
            p.TryPredict(10, .02f, out _);
            p.TryPredict(20, .02f, out _);
            Assert.That(p.Reconcile(p.Generation, 1, 1, .1f), Is.EqualTo(PredictionReconcileResult.Applied));
            Assert.That(p.State, Is.EqualTo(.5f).Within(.00001));
            Assert.That(p.PendingInputCount, Is.EqualTo(1));
            Assert.That(p.LastAcknowledgedSequence, Is.EqualTo(1));
            Assert.That(calls.Count, Is.EqualTo(3));
            Assert.That(calls[2].IsReplay, Is.True);
            Assert.That(calls[2].Sequence, Is.EqualTo(2));
            Assert.That(calls[0].IsReplay, Is.False);
        }

        [Test]
        public void Newer_snapshot_with_same_watermark_is_applied_and_zero_ack_replays_every_input()
        {
            var p = Create(); p.TryPredict(10, .02f, out _);
            p.Reconcile(p.Generation, 1, 0, 1);
            Assert.That(p.State, Is.EqualTo(1.2f).Within(.00001));
            p.Reconcile(p.Generation, 2, 0, 2);
            Assert.That(p.State, Is.EqualTo(2.2f).Within(.00001));
        }

        [Test]
        public void Duplicate_stale_and_impossible_acknowledgements_do_not_change_state()
        {
            var p = Create(); p.TryPredict(1, .02f, out _); p.TryPredict(1, .02f, out _);
            p.Reconcile(p.Generation, 10, 1, 2);
            Assert.That(p.Reconcile(p.Generation, 10, 2, 100), Is.EqualTo(PredictionReconcileResult.Stale));
            Assert.That(p.Reconcile(p.Generation, 9, 2, 100), Is.EqualTo(PredictionReconcileResult.Stale));
            Assert.That(p.Reconcile(p.Generation, 11, 0, 100), Is.EqualTo(PredictionReconcileResult.InvalidAcknowledgement));
            Assert.That(p.Reconcile(p.Generation, 11, 3, 100), Is.EqualTo(PredictionReconcileResult.InvalidAcknowledgement));
            Assert.That(p.State, Is.EqualTo(2.02f).Within(.00001));
            Assert.That(p.LastSnapshotTick, Is.EqualTo(10));
            Assert.That(p.PendingInputCount, Is.EqualTo(1));
        }

        [Test]
        public void Full_history_refuses_prediction_without_losing_commands_and_recovers_after_ack()
        {
            var p = Create(2); p.TryPredict(1, .02f, out _); p.TryPredict(2, .02f, out _);
            Assert.That(p.TryPredict(100, .02f, out _), Is.False);
            Assert.That(p.LastInputSequence, Is.EqualTo(2));
            Assert.That(p.State, Is.EqualTo(.06f).Within(.00001));
            p.Reconcile(p.Generation, 1, 1, .02f);
            Assert.That(p.TryPredict(3, .02f, out var next), Is.True);
            Assert.That(next.Sequence, Is.EqualTo(3));
            Assert.That(p.State, Is.EqualTo(.12f).Within(.00001));
        }

        [Test]
        public void Reset_fences_old_connection_and_respawn_responses_even_when_sequence_numbers_repeat()
        {
            var p = Create(); p.TryPredict(1, .02f, out var old);
            p.Reset(50, 20);
            p.TryPredict(2, .02f, out var current);
            Assert.That(current.Sequence, Is.EqualTo(old.Sequence));
            Assert.That(current.Generation, Is.Not.EqualTo(old.Generation));
            Assert.That(p.Reconcile(old.Generation, 999, 1, 0), Is.EqualTo(PredictionReconcileResult.Stale));
            Assert.That(p.State, Is.EqualTo(50.04f).Within(.00001));
            p.Reset(60, 30, 100);
            p.TryPredict(1, .02f, out current);
            Assert.That(current.Sequence, Is.EqualTo(101));
        }

        [TestCase(0f)] [TestCase(-1f)] [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)] [TestCase(.101f)]
        public void Invalid_step_does_not_consume_a_sequence(float dt)
        {
            var p = Create();
            Assert.Throws<ArgumentOutOfRangeException>(() => p.TryPredict(1, dt, out _));
            Assert.That(p.LastInputSequence, Is.Zero);
            Assert.That(p.State, Is.Zero);
        }

        [Test]
        public void Simulation_failure_during_prediction_or_replay_leaves_the_session_unchanged()
        {
            var fail = false;
            var p = Create(simulate: (s, i, c) => fail ? throw new InvalidOperationException() : s + i * c.DeltaTime);
            fail = true;
            Assert.Throws<InvalidOperationException>(() => p.TryPredict(1, .02f, out _));
            Assert.That(p.LastInputSequence, Is.Zero);
            fail = false; p.TryPredict(1, .02f, out _);
            fail = true;
            Assert.Throws<InvalidOperationException>(() => p.Reconcile(p.Generation, 1, 0, 5));
            Assert.That(p.State, Is.EqualTo(.02f).Within(.00001));
            Assert.That(p.LastSnapshotTick, Is.Zero);
            Assert.That(p.PendingInputCount, Is.EqualTo(1));
        }

        private sealed class Box { public float Value; }

        [Test]
        public void Baseline_and_constructor_validation_prevent_uninitialized_or_overflowed_commands()
        {
            var p = new PlayServPrediction<float, float>((s, i, c) => s + i, s => s, i => i);
            Assert.Throws<InvalidOperationException>(() => p.TryPredict(1, .02f, out _));
            Assert.Throws<InvalidOperationException>(() => p.Reconcile(0, 1, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => p.Reset(0, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => p.Reset(0, 0, -1));
            p.Reset(5, 0, long.MaxValue);
            Assert.Throws<OverflowException>(() => p.TryPredict(1, .02f, out _));
            Assert.That(p.State, Is.EqualTo(5));
            Assert.That(p.PendingInputCount, Is.Zero);
            Assert.Throws<ArgumentOutOfRangeException>(() => Create(0));
            Assert.Throws<ArgumentNullException>(() => new PlayServPrediction<float, float>(null, s => s, i => i));
        }

        [Test]
        public void Copy_failure_keeps_history_and_generation_and_original_objects_intact()
        {
            var fail = false;
            var p = new PlayServPrediction<Box, Box>((s, i, c) => { s.Value += i.Value; return s; },
                s => fail ? throw new InvalidOperationException() : new Box { Value = s.Value },
                i => new Box { Value = i.Value });
            p.Reset(new Box(), 0); var generation = p.Generation;
            fail = true;
            Assert.Throws<InvalidOperationException>(() => p.Reset(new Box { Value = 100 }, 5));
            Assert.Throws<InvalidOperationException>(() => p.TryPredict(new Box(), .02f, out _));
            fail = false;
            Assert.That(p.Generation, Is.EqualTo(generation));
            Assert.That(p.State.Value, Is.Zero);
            Assert.That(p.PendingInputCount, Is.Zero);
        }

        [Test]
        public void Several_corrections_in_one_frame_keep_the_same_visual_pose()
        {
            var s = new PlayServPredictionSmoothing(.1f, 10);
            s.Correct(Vector3.right * 2, Quaternion.identity, Vector3.right, Quaternion.identity);
            s.Apply(Vector3.right, Quaternion.identity, 0, out var rendered, out var rotation);
            s.Correct(rendered, rotation, Vector3.zero, Quaternion.identity);
            s.Apply(Vector3.zero, Quaternion.identity, 0, out rendered, out _);
            Assert.That(rendered.x, Is.EqualTo(2));
            Assert.Throws<ArgumentException>(() => s.Correct(new Vector3(float.NaN, 0, 0), Quaternion.identity, Vector3.zero, Quaternion.identity));
            Assert.Throws<ArgumentException>(() => s.Apply(Vector3.zero, new Quaternion(0, 0, 0, 0), 0, out _, out _));
            s.Apply(Vector3.zero, Quaternion.identity, 0, out rendered, out _);
            Assert.That(rendered.x, Is.EqualTo(2));
        }

        [Test]
        public void Mutable_states_and_inputs_are_copied_at_every_ownership_boundary()
        {
            var p = new PlayServPrediction<Box, Box>((s, i, c) => { s.Value += i.Value * c.DeltaTime; i.Value = 999; return s; },
                s => new Box { Value = s.Value }, i => new Box { Value = i.Value });
            var initial = new Box(); p.Reset(initial, 0); initial.Value = 100;
            var input = new Box { Value = 10 }; p.TryPredict(input, .02f, out var command);
            input.Value = 500; command.Input.Value = 600; p.State.Value = 700;
            var authoritative = new Box { Value = 1 };
            p.Reconcile(p.Generation, 1, 0, authoritative);
            authoritative.Value = 800;
            Assert.That(p.State.Value, Is.EqualTo(1.2f).Within(.00001));
        }

        [Test]
        public void Reentrant_mutation_is_rejected_without_resetting_the_session()
        {
            PlayServPrediction<float, float> p = null;
            p = Create(simulate: (s, i, c) => { p.Reset(50, 0); return 0; });
            Assert.Throws<InvalidOperationException>(() => p.TryPredict(1, .02f, out _));
            Assert.That(p.State, Is.Zero);
            Assert.That(p.LastInputSequence, Is.Zero);
        }

        [Test]
        public void Two_hundred_ms_round_trip_and_jitter_do_not_delay_local_input_or_repeat_effects()
        {
            var effects = new HashSet<long>();
            var p = Create(simulate: (s, i, c) => { if (!c.IsReplay) Assert.That(effects.Add(c.Sequence), Is.True); return s + i * c.DeltaTime; });
            var replies = new List<(int due, long seq, float state)>();
            float server = 0;
            for (var frame = 1; frame <= 80; frame++)
            {
                var input = frame <= 40 ? 1 : -1;
                var before = p.State;
                Assert.That(p.TryPredict(input, .02f, out var command), Is.True);
                Assert.That(p.State - before, Is.EqualTo(input * .02f).Within(.0001));
                server += command.Input * command.DeltaTime;
                replies.Add((frame + 10 + (frame % 3), command.Sequence, server));
                for (var i = replies.Count - 1; i >= 0; i--)
                    if (replies[i].due <= frame)
                    {
                        var r = replies[i]; p.Reconcile(p.Generation, r.seq, r.seq, r.state); replies.RemoveAt(i);
                    }
            }
            p.Reconcile(p.Generation, 81, 80, server);
            Assert.That(p.State, Is.EqualTo(server).Within(.0001));
            Assert.That(p.PendingInputCount, Is.Zero);
            Assert.That(effects.Count, Is.EqualTo(80));
        }

        [Test]
        public void Visual_correction_preserves_continuity_while_new_input_keeps_moving()
        {
            var s = new PlayServPredictionSmoothing(.1f, 10, 90);
            s.Correct(Vector3.right, Quaternion.Euler(0, 20, 0), Vector3.zero, Quaternion.identity);
            s.Apply(Vector3.zero, Quaternion.identity, 0, out var position, out var rotation);
            Assert.That(position.x, Is.EqualTo(1));
            Assert.That(Quaternion.Angle(rotation, Quaternion.Euler(0, 20, 0)), Is.LessThan(.001));
            s.Apply(Vector3.right, Quaternion.identity, .1f, out position, out rotation);
            Assert.That(position.x, Is.EqualTo(1.5f).Within(.001));
            Assert.That(Quaternion.Angle(rotation, Quaternion.identity), Is.EqualTo(10).Within(.01));
            s.Reset(); s.Apply(Vector3.zero, Quaternion.identity, 0, out position, out rotation);
            Assert.That(position, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void Large_corrections_snap_and_invalid_time_is_rejected()
        {
            var s = new PlayServPredictionSmoothing(.1f, 2, 45);
            s.Correct(Vector3.right * 3, Quaternion.identity, Vector3.zero, Quaternion.identity);
            s.Apply(Vector3.zero, Quaternion.identity, 0, out var position, out _);
            Assert.That(position, Is.EqualTo(Vector3.zero));
            s.Correct(Vector3.right, Quaternion.Euler(0, 90, 0), Vector3.zero, Quaternion.identity);
            s.Apply(Vector3.zero, Quaternion.identity, 0, out position, out var rotation);
            Assert.That(position, Is.EqualTo(Vector3.zero));
            Assert.That(Quaternion.Angle(rotation, Quaternion.identity), Is.Zero);
            Assert.Throws<ArgumentOutOfRangeException>(() => s.Apply(Vector3.zero, Quaternion.identity, float.NaN, out _, out _));
        }
    }
}
