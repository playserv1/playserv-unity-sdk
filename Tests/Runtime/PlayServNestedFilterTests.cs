using System;
using System.Linq;
using System.Linq.Expressions;
using NUnit.Framework;
using Playserv.Data;
using Playserv.DataSubscription;
using Playserv.Schema;
using Playserv.Serialization;

namespace Playserv.Tests.Runtime
{
    public sealed partial class PlayServTypedRecordsTests
    {
        [Test]
        public void Typed_nested_filters_share_wire_names_in_rest_and_realtime()
        {
            const string region = "eu\"quoted";
            var fake = new FakeDataHttpClient();
            fake.EnqueueJson(200, QueryPageJson());
            var set = CreateSet<NestedFilterModel>(fake, "ent_inventory");
            var query = new PlayServRecordQuery<NestedFilterModel>()
                .Where(x => x.Profile.Region == region)
                .Where(x => x.Profile.Level, PlayServQueryOperator.Gte, 7);
            set.QueryAsync(query).GetAwaiter().GetResult();
            var body = fake.Requests.Single().JsonBody;
            Assert.That(body, Does.Contain("\"field\":\"profile_wire.region_json\""));
            Assert.That(body, Does.Contain("\"field\":\"profile_wire.level_wire\""));
            var realtime = QueryBuilder.BuildCollectionQuery<NestedFilterModel>("Player", query.Snapshot());
            Assert.That(realtime.Query, Does.Contain("profile_wire.region_json: { eq: $filter0 }"));
            Assert.That(realtime.Query, Does.Contain("profile_wire.level_wire: { gte: $filter1 }"));
            Assert.That(realtime.Query, Does.Not.Contain(region));
            Assert.That(realtime.Variables["filter0"], Is.EqualTo(region));
            Assert.That(realtime.Variables["filter1"], Is.EqualTo(7));
        }

        [Test]
        public void Nested_filters_support_captured_contains_reversed_comparisons_and_or_groups()
        {
            var allowed = new[] { "eu", "us" };
            var query = new PlayServRecordQuery<NestedFilterModel>()
                .Where(x => allowed.Contains(x.Profile.Region) && 4 < x.Profile.Level)
                .Or(x => x.Profile.Region == "eu", x => x.Profile.Level >= 9);
            var snapshot = query.Snapshot();
            Assert.That(snapshot.Filters[0].Field, Is.EqualTo("profile_wire.region_json"));
            Assert.That(snapshot.Filters[0].Operator, Is.EqualTo(PlayServQueryOperator.In));
            Assert.That(snapshot.Filters[1].Operator, Is.EqualTo(PlayServQueryOperator.Gt));
            Assert.Throws<PlayServQueryCapabilityException>(() => QueryBuilder.BuildCollectionQuery<NestedFilterModel>("Player", snapshot));
            var realtimeQuery = new PlayServRecordQuery<NestedFilterModel>()
                .Where(x => 4 < x.Profile.Level)
                .Or(x => x.Profile.Region == "eu", x => x.Profile.Level >= 9);
            var payload = QueryBuilder.BuildCollectionQuery<NestedFilterModel>("Player", realtimeQuery.Snapshot());
            Assert.That(payload.Query, Does.Contain("profile_wire.region_json: { eq: $or0Filter0 }"));
            Assert.That(payload.Variables["or1Filter0"], Is.EqualTo(9));
        }

        [Test]
        public void Nested_filter_depth_is_eight_and_other_selector_capabilities_are_unchanged()
        {
            var parameter = Expression.Parameter(typeof(FilterNode), "x");
            Expression current = parameter;
            for (var index = 0; index < 7; index++) current = Expression.Property(current, nameof(FilterNode.Next));
            var eight = Expression.Lambda<Func<FilterNode, int>>(Expression.Property(current, nameof(FilterNode.Value)), parameter);
            var query = new PlayServRecordQuery<FilterNode>().Where(eight, PlayServQueryOperator.Eq, 1);
            QueryBuilder.BuildCollectionQuery<FilterNode>("Node", query.SelectFields("value").Snapshot());
            current = Expression.Property(current, nameof(FilterNode.Next));
            var nine = Expression.Lambda<Func<FilterNode, int>>(Expression.Property(current, nameof(FilterNode.Value)), parameter);
            Assert.Throws<ArgumentException>(() => new PlayServRecordQuery<FilterNode>().Where(nine, PlayServQueryOperator.Eq, 1));
            Assert.Throws<ArgumentException>(() => new PlayServRecordQuery<NestedFilterModel>().Where(x => x.Ignored.Region == "eu"));
            Assert.Throws<ArgumentException>(() => new PlayServRecordQuery<NestedFilterModel>().Where(x => x.Profile.Ignored == "eu"));
            Assert.Throws<ArgumentException>(() => new PlayServRecordQuery<NestedFilterModel>().OrderBy(x => x.Profile.Region));
            Assert.Throws<ArgumentException>(() => PlayServNaturalKey<NestedFilterModel>.For(x => x.Profile.Region, "eu"));
            Assert.Throws<ArgumentException>(() => QueryBuilder.BuildCollectionQuery<FilterNode>("Bad.Name", query.Snapshot()));
        }

        [TestCase("profile..region")]
        [TestCase("profile.region: { eq: 1 }")]
        [TestCase("a.b.c.d.e.f.g.h.i")]
        public void Realtime_nested_filter_validation_does_not_accept_query_injection(string field)
        {
            var query = new PlayServRecordQuery<NestedFilterModel>().Where(field, PlayServQueryOperator.Eq, "eu");
            Assert.Throws<ArgumentException>(() => QueryBuilder.BuildCollectionQuery<NestedFilterModel>("Player", query.Snapshot()));
        }

        private sealed class NestedFilterModel
        {
            [PlayServField("profile_wire")] public NestedFilterProfile Profile { get; set; }
            [PlayServIgnore] public NestedFilterProfile Ignored { get; set; }
        }
        private sealed class NestedFilterProfile
        {
            [PlayServField("region_schema"), PlayServJsonName("region_json")] public string Region { get; set; }
            [PlayServField("level_wire")] public int Level { get; set; }
            [PlayServIgnore] public string Ignored { get; set; }
        }
        private sealed class FilterNode
        {
            [PlayServField("next")] public FilterNode Next { get; set; }
            [PlayServField("value")] public int Value { get; set; }
        }
    }
}
