using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

namespace Playserv.Tests.Runtime
{
    public sealed class PlayServDataFieldCallbackTests
    {
        [Test]
        public void Field_callback_observes_initial_and_complete_installed_state_without_extra_requests()
        {
            using var fixture = new WireFixture();
            using var entity = fixture.OpenRow();
            var changes = new ChangeSink();
            var count = fixture.Commands.Count;
            using var observer = Observe(entity, "health", changes);
            Assert.That(changes.Values.Count, Is.EqualTo(1));
            Assert.That(Property<bool>(changes.Values[0], "IsInitial"), Is.True);
            Assert.That(Property<bool>(changes.Values[0], "HasPrevious"), Is.False);
            var installedSpeed = 0;
            changes.Inspect = _ => installedSpeed = entity.Value.Speed;
            fixture.Update(new { Id = "one", health = 5, speed = 20 }, "2");
            Assert.That(changes.Values.Count, Is.EqualTo(2));
            Assert.That(Property<object>(changes.Values[1], "Previous"), Is.EqualTo(10));
            Assert.That(Property<object>(changes.Values[1], "Current"), Is.EqualTo(5));
            Assert.That(fixture.Commands.Count, Is.EqualTo(count));
            Assert.That(installedSpeed, Is.EqualTo(20));
        }

        private static T Property<T>(object value, string name) => (T)value.GetType().GetProperty(name).GetValue(value);

        [Test]
        public void Coalesced_changes_ignore_unrelated_equal_stale_and_duplicate_frames()
        {
            using var fixture = new WireFixture();
            using var entity = fixture.OpenRow();
            var changes = new List<PlayServFieldChange<Row>>();
            using var observer = entity.ObserveField("health", changes.Add);
            fixture.Update(new { Id = "one", health = 10, speed = 2 }, "2");
            fixture.Update(new { Id = "one", health = 3, speed = 3 }, "8");
            fixture.Update(new { Id = "one", health = 99 }, "7");
            fixture.Update(new { Id = "one", health = 99 }, "8");
            Assert.That(changes.Count, Is.EqualTo(2));
            Assert.That(changes[1].Previous, Is.EqualTo(10));
            Assert.That(changes[1].Current, Is.EqualTo(3));
        }

        [Test]
        public void New_epoch_baseline_compares_with_retained_state_not_an_empty_view()
        {
            using var fixture = new WireFixture();
            using var entity = fixture.OpenRow();
            var changes = new List<PlayServFieldChange<Row>>();
            using var observer = entity.ObserveField("health", changes.Add);
            fixture.Emit("DataSubscriptionUpdate", new { RequestId = 0, DataSubscriptionId = 10, UpdateType = "Reset", Authority = new { Epoch = "epoch-a", Sequence = "2", Complete = false, Reason = "base_unavailable" } });
            fixture.Epoch = "epoch-b";
            fixture.Update(new { Id = "one", health = 10, speed = 2 }, "1");
            Assert.That(changes.Count, Is.EqualTo(1));
            fixture.Update(new { Id = "one", health = 4, speed = 2 }, "2");
            Assert.That(changes[1].IsInitial, Is.False);
            Assert.That(changes[1].Previous, Is.EqualTo(10));
        }

        [Test]
        public void Missing_null_and_nested_json_values_remain_distinct_and_detached_from_the_model()
        {
            using var fixture = new WireFixture();
            using var entity = fixture.OpenRow();
            var changes = new List<PlayServFieldChange<Row>>();
            using var observer = entity.ObserveField("extra", changes.Add);
            Assert.That(changes[0].HasCurrent, Is.False);
            fixture.Update(new { Id = "one", health = 10, extra = (object)null }, "2");
            Assert.That(changes[1].HasCurrent, Is.True);
            Assert.That(changes[1].Current, Is.Null);
            fixture.Update(new { Id = "one", health = 10, extra = new { nested = new[] { 1, 2 } } }, "3");
            var detached = (IDictionary<string, object>)changes[2].Current;
            detached["nested"] = "changed";
            fixture.Update(new { Id = "one", health = 10, extra = new { nested = new[] { 1, 2 } } }, "4");
            Assert.That(changes.Count, Is.EqualTo(3));
            fixture.Update(new { Id = "one", health = 10 }, "5");
            Assert.That(changes[3].HasPrevious, Is.True);
            Assert.That(changes[3].HasCurrent, Is.False);
        }

        [Test]
        public void Throwing_observers_and_mutating_payloads_do_not_corrupt_other_observers()
        {
            using var fixture = new WireFixture();
            using var entity = fixture.OpenRow();
            using var bad = entity.ObserveField("health", change => { change.Value.Health = 500; throw new Exception("private-payload"); });
            var changes = new List<PlayServFieldChange<Row>>();
            using var good = entity.ObserveField("health", changes.Add);
            fixture.Update(new { Id = "one", health = 5, speed = 20 }, "2");
            Assert.That(entity.Value.Health, Is.EqualTo(5));
            Assert.That(changes[1].Previous, Is.EqualTo(10));
            Assert.That(changes[1].Value.Health, Is.EqualTo(5));
        }

        [Test]
        public void Disposing_another_observer_during_delivery_prevents_its_callback()
        {
            using var fixture = new WireFixture();
            using var entity = fixture.OpenRow();
            IDisposable second = null;
            using var first = entity.ObserveField("health", change => { if (!change.IsInitial) second.Dispose(); });
            var changes = new List<PlayServFieldChange<Row>>();
            second = entity.ObserveField("health", changes.Add);
            fixture.Update(new { Id = "one", health = 5 }, "2");
            Assert.That(changes.Count, Is.EqualTo(1));
            entity.Dispose();
            fixture.Update(new { Id = "one", health = 4 }, "3");
            Assert.Throws<ObjectDisposedException>(() => entity.ObserveField("health", changes.Add));
        }

        [TestCase("")] [TestCase(" ")] [TestCase("nested.health")] [TestCase("/health")] [TestCase("health[0]")]
        public void Only_exact_top_level_wire_names_are_accepted(string field)
        {
            using var fixture = new WireFixture();
            using var entity = fixture.OpenRow();
            Assert.Throws<ArgumentException>(() => entity.ObserveField(field, _ => { }));
        }

        [Test]
        public void Collection_rows_use_wire_keys_reorder_silently_and_report_view_removal()
        {
            using var fixture = new WireFixture { Collection = true };
            using var collection = fixture.OpenCollection();
            var changes = new List<PlayServFieldChange<Row>>();
            using var observer = collection.ObserveField("health", changes.Add);
            fixture.Update(new { Inventory = new[] { new { Id = "two", health = 30 }, new { Id = "one", health = 10 } } }, "2");
            Assert.That(changes.Count, Is.EqualTo(2));
            Assert.That(changes[1].RowId, Is.EqualTo("two"));
            fixture.Update(new { Inventory = new[] { new { Id = "one", health = 10 }, new { Id = "two", health = 30 } } }, "3");
            Assert.That(changes.Count, Is.EqualTo(2));
            fixture.Update(new { Inventory = new[] { new { Id = "two", health = 20 } } }, "4");
            Assert.That(changes.Count, Is.EqualTo(4));
            Assert.That(changes.Last().IsRemovedFromView, Is.True);
            Assert.That(changes.Last().Value, Is.Null);
            Assert.That(collection.Items.Single().Health, Is.EqualTo(20));
        }

        [Test]
        public void Collection_natural_key_is_explicit_and_bad_identity_cannot_advance_observer_baseline()
        {
            using var fixture = new WireFixture { Collection = true };
            using var collection = fixture.OpenCollection();
            fixture.Update(new { Inventory = new[] { new { PlayerId = "a", health = 10 } } }, "2");
            Assert.Throws<ArgumentException>(() => collection.ObserveField("health", _ => { }));
            var changes = new List<PlayServFieldChange<Row>>();
            using var observer = collection.ObserveField("health", changes.Add, "PlayerId");
            fixture.Update(new { Inventory = new[] { new { PlayerId = "a", health = 5 }, new { PlayerId = "a", health = 6 } } }, "3");
            Assert.That(changes.Count, Is.EqualTo(1));
            fixture.Update(new { Inventory = new[] { new { PlayerId = "a", health = 4 } } }, "4");
            Assert.That(changes[1].Previous, Is.EqualTo(10));
            Assert.That(changes[1].RowId, Is.EqualTo("a"));
        }

        [TestCase(null)]
        [TestCase("")]
        public void Collection_observation_cannot_disable_stable_row_identity(string rowKeyField)
        {
            using var fixture = new WireFixture { Collection = true };
            using var collection = fixture.OpenCollection();
            Assert.Throws<ArgumentException>(() => collection.ObserveField("health", _ => { }, rowKeyField));
        }

        [Test]
        public void Reentrant_updates_deliver_batches_in_order_to_every_observer()
        {
            using var fixture = new WireFixture();
            using var entity = fixture.OpenRow();
            using var first = entity.ObserveField("health", change =>
            {
                if (!change.IsInitial && Convert.ToInt32(change.Current) == 5)
                    fixture.Update(new { Id = "one", health = 4 }, "3");
            });
            var values = new List<int>();
            using var second = entity.ObserveField("health", change => values.Add(Convert.ToInt32(change.Current)));
            fixture.Update(new { Id = "one", health = 5 }, "2");
            Assert.That(values, Is.EqualTo(new[] { 10, 5, 4 }));
        }

        [Test]
        public void Legacy_patch_preserves_unknown_wire_fields_and_does_not_observe_local_edits()
        {
            using var fixture = new WireFixture { Ungoverned = true };
            using var entity = fixture.OpenRow();
            var changes = new List<PlayServFieldChange<Row>>();
            using var observer = entity.ObserveField("extra", changes.Add);
            fixture.Update(new { Id = "one", health = 10, extra = "old" }, "2");
            entity.Value.Health = 900;
            fixture.Emit("DataSubscriptionUpdate", new { RequestId = 0, DataSubscriptionId = 10, UpdateType = "Patch", IsCollection = false,
                Data = new[] { new { op = "replace", path = "/extra", value = "new" } } });
            Assert.That(changes.Count, Is.EqualTo(3));
            Assert.That(changes[2].Previous, Is.EqualTo("old"));
            Assert.That(changes[2].Current, Is.EqualTo("new"));
        }

        [Test]
        public void Queued_initial_and_background_updates_run_on_captured_context_and_stop_after_disposal()
        {
            var original = SynchronizationContext.Current;
            var context = new QueuedContext();
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                using var fixture = new WireFixture();
                using var entity = fixture.OpenRow();
                var changes = new List<PlayServFieldChange<Row>>();
                IDisposable observer = null;
                Task.Run(() => observer = entity.ObserveField("health", change =>
                {
                    Assert.That(SynchronizationContext.Current, Is.SameAs(context));
                    changes.Add(change);
                })).GetAwaiter().GetResult();
                Assert.That(changes, Is.Empty);
                observer.Dispose();
                context.Drain();
                Assert.That(changes, Is.Empty);
                using var active = entity.ObserveField("health", changes.Add);
                Task.Run(() => fixture.Update(new { Id = "one", health = 5 }, "2")).GetAwaiter().GetResult();
                Assert.That(entity.Value.Health, Is.EqualTo(10));
                context.Drain();
                Assert.That(changes.Count, Is.EqualTo(2));
                Assert.That(entity.Value.Health, Is.EqualTo(5));
                Task.Run(() => fixture.Update(new { Id = "one", health = 4 }, "3")).GetAwaiter().GetResult();
                entity.Dispose();
                context.Drain();
                Assert.That(changes.Count, Is.EqualTo(2));
            }
            finally { SynchronizationContext.SetSynchronizationContext(original); }
        }

        [Test]
        public void Termination_does_not_manufacture_field_removal_and_stops_observation()
        {
            using var fixture = new WireFixture();
            using var entity = fixture.OpenRow();
            var changes = new List<PlayServFieldChange<Row>>();
            using var observer = entity.ObserveField("health", changes.Add);
            fixture.Emit("DataSubscriptionUpdate", new { RequestId = 0, DataSubscriptionId = 10, UpdateType = "Terminated", ErrorCode = 49001,
                EntityType = "Inventory", IdValue = "one", Authority = new { Epoch = "epoch-a", Sequence = "2", Revision = "2" } });
            Assert.That(changes.Count, Is.EqualTo(1));
            Assert.Throws<ObjectDisposedException>(() => entity.ObserveField("health", changes.Add));
        }

        [TestCase("entity")]
        [TestCase("collection")]
        [TestCase("polling")]
        public void Adapter_disposal_suppresses_queued_initial_callbacks_and_rejects_new_observers(string kind)
        {
            var original = SynchronizationContext.Current;
            var context = new QueuedContext();
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                using var fixture = new WireFixture { Collection = kind == "collection" };
                using var entity = kind == "collection" ? null : kind == "polling" ? fixture.OpenPollingRow() : fixture.OpenRow();
                using var collection = kind == "collection" ? fixture.OpenCollection() : null;
                if (kind == "polling") entity.RefreshAsync().GetAwaiter().GetResult();
                var changes = new List<PlayServFieldChange<Row>>();
                Func<IDisposable> observe = () => collection != null
                    ? collection.ObserveField("health", changes.Add)
                    : entity.ObserveField("health", changes.Add);
                using var observer = Task.Run(observe).GetAwaiter().GetResult();
                Assert.That(changes, Is.Empty);
                fixture.Adapter.Dispose();
                context.Drain();
                Assert.That(changes, Is.Empty, "Closed SDK configurations cannot deliver queued field callbacks.");
                Assert.Throws<ObjectDisposedException>(() => observe());
            }
            finally { SynchronizationContext.SetSynchronizationContext(original); }
        }

        [Test]
        public void Builder_projection_observes_wire_names_and_delivers_detached_projected_value()
        {
            using var fixture = new WireFixture();
            using var entity = new SharedEntityBuilder<Row>(fixture.Adapter, "Inventory").Key("one").UseTransport()
                .Select(row => new Projection { DisplayHealth = row.Health }).BindAsync().GetAwaiter().GetResult();
            var changes = new List<PlayServFieldChange<Projection>>();
            using var observer = entity.ObserveField("health", changes.Add);
            fixture.Update(new { Id = "one", health = 4 }, "2");
            Assert.That(changes[0].Current, Is.EqualTo(10));
            Assert.That(changes[1].Value.DisplayHealth, Is.EqualTo(4));
            Assert.That(changes[1].RowId, Is.EqualTo("one"));
            changes[1].Value.DisplayHealth = 100;
            Assert.That(entity.Value.DisplayHealth, Is.EqualTo(4));
        }

        [Test]
        public void First_late_snapshot_is_initial_and_collection_install_is_atomic()
        {
            using var fixture = new WireFixture { Collection = true, SkipInitial = true };
            using var collection = fixture.OpenCollection();
            var counts = new List<int>();
            var initials = new List<bool>();
            using var observer = collection.ObserveField("health", change => { counts.Add(collection.Items.Count); initials.Add(change.IsInitial); });
            Assert.That(counts, Is.Empty);
            fixture.Update(new { Inventory = new[] { new { Id = "a", health = 1 }, new { Id = "b", health = 2 } } }, "1");
            Assert.That(counts, Is.EqualTo(new[] { 2, 2 }));
            Assert.That(initials, Is.EqualTo(new[] { true, true }));
        }

        [Test]
        public void Legacy_patch_preserves_existing_unrelated_local_view_edits()
        {
            using var fixture = new WireFixture { Ungoverned = true };
            using var entity = fixture.OpenRow();
            entity.Value.Speed = 900;
            fixture.Emit("DataSubscriptionUpdate", new { RequestId = 0, DataSubscriptionId = 10, UpdateType = "Patch", IsCollection = false,
                Data = new[] { new { op = "replace", path = "/health", value = 5 } } });
            Assert.That(entity.Value.Health, Is.EqualTo(5));
            Assert.That(entity.Value.Speed, Is.EqualTo(900), "Existing ungoverned patch behavior must not change.");
        }

        [TestCase("null", "\"\"")]
        [TestCase("1", "\"1\"")]
        [TestCase("true", "\"true\"")]
        [TestCase("9007199254740992", "9007199254740993")]
        [TestCase("[1,2]", "[2,1]")]
        [TestCase("{\"nested\":[1]}", "{\"nested\":[\"1\"]}")]
        public void Structural_field_comparison_preserves_json_types_array_order_and_large_integer_precision(string before, string after)
        {
            using var fixture = new WireFixture();
            using var entity = fixture.OpenRow();
            fixture.Update(new { Id = "one", extra = Newtonsoft.Json.Linq.JToken.Parse(before) }, "2");
            var changes = new List<PlayServFieldChange<Row>>();
            using var observer = entity.ObserveField("extra", changes.Add);
            fixture.Update(new { Id = "one", extra = Newtonsoft.Json.Linq.JToken.Parse(after) }, "3");
            Assert.That(changes.Count, Is.EqualTo(2));
        }

        [Test]
        public void Equivalent_objects_with_reordered_properties_do_not_notify()
        {
            using var fixture = new WireFixture();
            using var entity = fixture.OpenRow();
            fixture.Update(new { Id = "one", extra = Newtonsoft.Json.Linq.JToken.Parse("{\"a\":1,\"nested\":{\"b\":true,\"c\":[null,2]}}") }, "2");
            var changes = new List<PlayServFieldChange<Row>>();
            using var observer = entity.ObserveField("extra", changes.Add);
            fixture.Update(new { Id = "one", extra = Newtonsoft.Json.Linq.JToken.Parse("{\"nested\":{\"c\":[null,2],\"b\":true},\"a\":1}") }, "3");
            Assert.That(changes.Count, Is.EqualTo(1));
        }

        [Test]
        public void Object_model_value_getter_returns_independent_snapshots() => AssertDetachedValue<object>();

        [Test]
        public void Dictionary_model_value_getter_returns_independent_snapshots() => AssertDetachedValue<Dictionary<string, object>>();

        private static void AssertDetachedValue<T>() where T : class, new()
        {
            using var fixture = new WireFixture();
            using var entity = fixture.OpenRow<T>();
            var changes = new List<PlayServFieldChange<T>>();
            using var observer = entity.ObserveField("health", changes.Add);
            var first = (IDictionary<string, object>)(object)changes[0].Value;
            first["health"] = 500;
            Assert.That(Convert.ToInt32(((IDictionary<string, object>)(object)changes[0].Value)["health"]), Is.EqualTo(10));
            fixture.Update(new { Id = "one", health = 5 }, "2");
            Assert.That(changes[1].Previous, Is.EqualTo(10));
        }

        [TestCase("add", false)]
        [TestCase("replace", false)]
        [TestCase("add", true)]
        [TestCase("replace", true)]
        public void Projected_patch_rebuilds_the_typed_value_from_installed_wire_state(string operation, bool customMapper)
        {
            using var fixture = new WireFixture { Ungoverned = true };
            using var entity = customMapper ? fixture.OpenProjection() : new SharedEntityBuilder<Row>(fixture.Adapter, "Inventory")
                .Key("one").UseTransport().Select(row => new Projection { DisplayHealth = row.Health }).BindAsync().GetAwaiter().GetResult();
            var changes = new List<PlayServFieldChange<Projection>>();
            using var observer = entity.ObserveField("health", changes.Add);
            fixture.Emit("DataSubscriptionUpdate", new { RequestId = 0, DataSubscriptionId = 10, UpdateType = "Patch", IsCollection = false,
                Data = new[] { new { op = operation, path = "/health", value = 5 } } });
            Assert.That(changes.Count, Is.EqualTo(2));
            Assert.That(changes[1].Current, Is.EqualTo(5));
            Assert.That(changes[1].Value.DisplayHealth, Is.EqualTo(5));
            Assert.That(entity.Value.DisplayHealth, Is.EqualTo(5));
        }

        [TestCase("entity")]
        [TestCase("collection")]
        [TestCase("polling")]
        public void Reentrant_field_callbacks_preserve_existing_Changed_delivery(string kind)
        {
            using var fixture = new WireFixture { Collection = kind == "collection", PollHealth = 5 };
            using var entity = kind == "collection" ? null : kind == "polling" ? fixture.OpenPollingRow() : fixture.OpenRow();
            using var collection = kind == "collection" ? fixture.OpenCollection() : null;
            var whole = new List<int>();
            if (collection != null) collection.Changed += rows => whole.Add(rows.Single().Health);
            else entity.Changed += row => whole.Add(row.Health);
            Action<PlayServFieldChange<Row>> callback = change =>
            {
                if (Convert.ToInt32(change.Current) != 5) return;
                if (kind == "polling") entity.Value.Health = 4;
                else fixture.Update(collection != null ? (object)new { Inventory = new[] { new { Id = "one", health = 4 } } }
                    : new { Id = "one", health = 4 }, "3");
            };
            using var observer = collection != null ? collection.ObserveField("health", callback) : entity.ObserveField("health", callback);
            if (kind == "polling") entity.RefreshAsync().GetAwaiter().GetResult();
            else fixture.Update(collection != null ? (object)new { Inventory = new[] { new { Id = "one", health = 5 } } }
                : new { Id = "one", health = 5 }, "2");
            Assert.That(whole, Is.EqualTo(kind == "polling" ? new[] { 5 } : new[] { 5, 4 }));
        }

        [TestCase("entity")]
        [TestCase("collection")]
        [TestCase("polling")]
        public void Throwing_Changed_callback_cannot_suppress_the_prepared_field_notification(string kind)
        {
            using var fixture = new WireFixture { Collection = kind == "collection", PollHealth = 5 };
            using var entity = kind == "collection" ? null : kind == "polling" ? fixture.OpenPollingRow() : fixture.OpenRow();
            using var collection = kind == "collection" ? fixture.OpenCollection() : null;
            var changes = new List<PlayServFieldChange<Row>>();
            using var observer = collection != null ? collection.ObserveField("health", changes.Add) : entity.ObserveField("health", changes.Add);
            if (collection != null) collection.Changed += _ => throw new InvalidOperationException("private-payload");
            else entity.Changed += _ => throw new InvalidOperationException("private-payload");
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Error,
                "[PlayServ][data] " + (kind == "entity" ? "Unexpected error processing transport update" :
                    kind == "polling" ? "Failed to map polling payload" : "Failed to map collection payload") + ": InvalidOperationException");
            if (kind == "polling") entity.RefreshAsync().GetAwaiter().GetResult();
            else fixture.Update(collection != null ? (object)new { Inventory = new[] { new { Id = "one", health = 5 } } }
                : new { Id = "one", health = 5 }, "2");
            Assert.That(changes.Count, Is.EqualTo(kind == "polling" ? 1 : 2));
            Assert.That(changes.Last().Current, Is.EqualTo(5));
        }

        [Test]
        public void Registration_and_update_inside_Changed_cannot_overtake_prepared_batches()
        {
            using var fixture = new WireFixture();
            using var entity = fixture.OpenRow();
            var order = new List<string>();
            using var observer = entity.ObserveField("health", change =>
            {
                if (!change.IsInitial) order.Add("field:" + change.Current);
            });
            IDisposable late = null;
            entity.Changed += row =>
            {
                order.Add("changed:" + row.Health);
                if (row.Health != 5) return;
                late = entity.ObserveField("health", change => order.Add("late:" + change.Current));
                order.Add("registered");
                fixture.Update(new { Id = "one", health = 4 }, "3");
                order.Add("returned");
            };
            fixture.Update(new { Id = "one", health = 5 }, "2");
            Assert.That(order, Is.EqualTo(new[] { "changed:5", "registered", "changed:4", "returned", "field:5", "late:5", "field:4", "late:4" }));
            late.Dispose();
        }

        [Serializable]
        private sealed class Projection { public int DisplayHealth; }

        private sealed class QueuedContext : SynchronizationContext
        {
            private readonly System.Collections.Concurrent.ConcurrentQueue<Action> _pending = new System.Collections.Concurrent.ConcurrentQueue<Action>();
            public override void Post(SendOrPostCallback callback, object state) => _pending.Enqueue(() => callback(state));
            public void Drain() { while (_pending.TryDequeue(out var action)) action(); }
        }

        private static IDisposable Observe(ISharedEntity<Row> entity, string field, ChangeSink sink)
        {
            var type = typeof(ISharedEntity<>).Assembly.GetType("Playserv.DataSubscription.PlayServFieldObservationExtensions");
            Assert.That(type, Is.Not.Null, "Subscribed entities must expose the field-observation API.");
            var method = type.GetMethods().Single(item => item.Name == "ObserveField" &&
                item.GetParameters()[0].ParameterType.GetGenericTypeDefinition() == typeof(ISharedEntity<>)).MakeGenericMethod(typeof(Row));
            var callbackType = method.GetParameters()[2].ParameterType;
            var record = typeof(ChangeSink).GetMethod(nameof(ChangeSink.Record)).MakeGenericMethod(callbackType.GetGenericArguments()[0]);
            return (IDisposable)method.Invoke(null, new object[] { entity, field, Delegate.CreateDelegate(callbackType, sink, record) });
        }

        private sealed class ChangeSink
        {
            public readonly List<object> Values = new List<object>();
            public Action<object> Inspect;
            public void Record<T>(T value) { Values.Add(value); Inspect?.Invoke(value); }
        }

        [Serializable]
        private sealed class Row
        {
            [Newtonsoft.Json.JsonProperty("health")] public int Health;
            [Newtonsoft.Json.JsonProperty("speed")] public int Speed;
        }

        private sealed class WireFixture : IPlayServCommandBus, IPlayServRuntimeIdentity, ITransportImplementation, IDisposable
        {
            private readonly Observable _incoming = new Observable();
            private readonly NewtonsoftJsonCodec _json = new NewtonsoftJsonCodec();
            private readonly Transport _transport;
            public readonly List<string> Commands = new List<string>();
            public PlayServDataSubscriptionAdapter Adapter { get; }
            public string PlayerId => "player-a";
            public PlayServState State => PlayServState.Online;
            public string Epoch = "epoch-a";
            public bool Collection;
            public bool Ungoverned;
            public bool SkipInitial;
            public int PollHealth = 10;

            public WireFixture()
            {
                CommandTypeProviderRegistry.Register(new DataSubscriptionCommandTypeProvider());
                _transport = new Transport(this, new Playserv.Proxy.Implementation.JsonSerializer(_json,
                    new NewtonsoftCommandPayloadMapper()), new RequestIdGenerator(), new CaptureLogger());
                Adapter = new PlayServDataSubscriptionAdapter(this, new CaptureLogger(), _json);
            }

            public ISharedEntity<Row> OpenRow()
                => OpenRow<Row>();

            public ISharedEntity<T> OpenRow<T>() where T : class, new()
            {
                const string query = "Inventory(id: $id) { Id health speed }";
                var variables = new Dictionary<string, object> { ["id"] = "one" };
                var lease = Adapter.AcquireTransportSubscriptionAsync(query, variables, "Inventory").GetAwaiter().GetResult();
                return new SharedEntity<T>(Adapter, lease, null, query, variables);
            }

            public ISharedEntity<Projection> OpenProjection()
            {
                const string query = "Inventory(id: $id) { Id health speed }";
                var variables = new Dictionary<string, object> { ["id"] = "one" };
                var lease = Adapter.AcquireTransportSubscriptionAsync(query, variables, "Inventory").GetAwaiter().GetResult();
                return new SharedEntity<Projection>(Adapter, lease, null, query, variables,
                    raw => new Projection { DisplayHealth = _json.Convert<Row>(raw).Health });
            }

            public ISharedCollection<Row> OpenCollection() => Adapter.SelectTypedCollectionAsync<Row>("Inventory { Id health speed PlayerId }", null, CancellationToken.None).GetAwaiter().GetResult();

            public ISharedEntity<Row> OpenPollingRow() => new SharedEntityBuilder<Row>(Adapter, "Inventory")
                .Key("one").UsePolling().BindAsync().GetAwaiter().GetResult();

            public IDisposable On<T>(Action<T> onNext) => _transport.OnReceive<T>().Subscribe(new Observer<T>(onNext));
            public IDisposable OnCommand(string name, Action<object> onNext) => _transport.OnReceive(name).Subscribe(new Observer<object>(onNext));
            public Task SendAsync<T>(T command, string moduleName = null) => _transport.Send(command, moduleName);
            public Task<bool> Connect() => Task.FromResult(true);
            public void ResetConnection() { }
            public IObservable<byte[]> OnReceive() => _incoming;
            public Task Send(byte[] bytes)
            {
                var frame = Newtonsoft.Json.Linq.JObject.Parse(Encoding.UTF8.GetString(bytes));
                var command = ((string)frame["Command"]).Split('.').Last();
                Commands.Add(command);
                var id = (long)frame["Payload"]["RequestId"];
                if (command == "DataAuthorityContextRequest")
                {
                    if (Ungoverned) Emit("DataAuthorityContextResponse", new { RequestId = id, Error = new { ErrorCode = 32001, Message = "disabled" } });
                    else Emit("DataAuthorityContextResponse", new { RequestId = id, Result = new { Capability = "authoritative_state_v1", Epoch, RetrySession = "fixture-session", RetryWindowMs = 120000 } });
                }
                if (command == "DataSubscriptionRequest")
                {
                    Emit("DataSubscriptionResponse", new { RequestId = id, Result = new { SubscriptionId = 10, Authority = new { Epoch } } });
                    var row = new { Id = "one", health = 10, speed = 1 };
                    if (!SkipInitial) Update(Collection ? (object)new { Inventory = new[] { row } } : row, "1", id);
                }
                if (command == "DataSubscriptionCloseRequest")
                    Emit("DataSubscriptionCloseResponse", new { RequestId = id, Result = new { Success = true } });
                if (command == "DataGetRequest")
                    Emit("DataGetResponse", new { RequestId = id, Result = new { Data = new { Inventory = new { Id = "one", health = PollHealth, speed = 1 } } } });
                return Task.CompletedTask;
            }

            public void Update(object data, string sequence, long requestId = 0) => Emit("DataSubscriptionUpdate", new
            {
                RequestId = requestId, DataSubscriptionId = 10, UpdateType = "Overwrite", IsCollection = Collection,
                EntityType = "Inventory", IdValue = Collection ? "*" : "one", Data = data,
                Authority = Ungoverned ? null : new { Epoch, Sequence = sequence, Revision = sequence, Complete = true }
            });
            public void Emit(string command, object payload) => _incoming.Emit(Encoding.UTF8.GetBytes(_json.Serialize(new { Command = "module_dataflow." + command, Payload = payload })));
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
