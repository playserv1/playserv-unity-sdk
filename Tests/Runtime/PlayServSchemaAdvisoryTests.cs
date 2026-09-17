using System;
using System.Threading;
using NUnit.Framework;
using Playserv.Data;
using Playserv.Serialization;

namespace Playserv.Tests.Runtime
{
    public sealed partial class PlayServTypedRecordsTests
    {
        [Test]
        public void SchemaAdvisoryUsesVisibleCatalogueWithoutMutationsOrPhysicalAbsenceClaims()
        {
            var http = new FakeDataHttpClient();
            http.EnqueueCatalogueJson("{\"data\":[{\"entity_id\":\"ent_inventory\",\"name\":\"inventoryitem\",\"singleton\":false}]}");
            var client = new PlayServRecordsClient(Settings(), http, new NewtonsoftJsonCodec());
            var result = client.CheckSchemaAsync(new[] { typeof(InventoryItem), typeof(PlayServSchemaAdvisory) }, default).GetAwaiter().GetResult();
            Assert.That(result[0].Availability, Is.EqualTo(PlayServSchemaAvailability.Visible));
            Assert.That(result[0].Table.EntityId, Is.EqualTo("ent_inventory"));
            Assert.That(result[1].Availability, Is.EqualTo(PlayServSchemaAvailability.NotVisible));
            Assert.That(result[1].Reason, Is.EqualTo("not_in_visible_catalogue"));
            Assert.That(http.AllRequests.Count, Is.EqualTo(1));
            Assert.That(http.AllRequests[0].Method, Is.EqualTo("GET"));
            Assert.That(http.AllRequests[0].RelativePath, Is.EqualTo("data/tables"));
        }

        [Test]
        public void SchemaAdvisoryUnavailableAndValidationAreSafe()
        {
            var http = new FakeDataHttpClient();
            http.EnqueueException(new PlayServDataException("private detail", 403, "forbidden"));
            var client = new PlayServRecordsClient(Settings(), http, new NewtonsoftJsonCodec());
            var result = client.CheckSchemaAsync(new[] { typeof(InventoryItem) }, default).GetAwaiter().GetResult();
            Assert.That(result[0].Availability, Is.EqualTo(PlayServSchemaAvailability.Unavailable));
            Assert.That(result[0].Reason, Is.EqualTo("catalogue_unavailable"));
            Assert.Throws<ArgumentException>(() => client.CheckSchemaAsync(new Type[] { null }, default).GetAwaiter().GetResult());
            Assert.Throws<OperationCanceledException>(() => client.CheckSchemaAsync(new[] { typeof(InventoryItem) }, new CancellationToken(true)).GetAwaiter().GetResult());
            Assert.That(client.CheckSchemaAsync(Array.Empty<Type>(), default).GetAwaiter().GetResult(), Is.Empty);
            Assert.That(http.AllRequests.Count, Is.EqualTo(1));
        }

        [Test]
        public void SchemaAdvisoryPreservesExactNamePreferenceAndRejectsAmbiguity()
        {
            var http = new FakeDataHttpClient();
            http.EnqueueCatalogueJson("{\"data\":[{\"entity_id\":\"ent_a\",\"name\":\"INVENTORYITEM\"},{\"entity_id\":\"ent_b\",\"name\":\"inventoryitem\"}]}");
            http.EnqueueCatalogueJson("{\"data\":[{\"entity_id\":\"ent_a\",\"name\":\"INVENTORYITEM\"},{\"entity_id\":\"ent_b\",\"name\":\"InventoryItem\"}]}");
            var client = new PlayServRecordsClient(Settings(), http, new NewtonsoftJsonCodec());
            Assert.That(client.CheckSchemaAsync(new[] { typeof(InventoryItem) }, default).GetAwaiter().GetResult()[0].Reason, Is.EqualTo("ambiguous_type_name"));
            Assert.That(client.CheckSchemaAsync(new[] { typeof(InventoryItem) }, default).GetAwaiter().GetResult()[0].Table.EntityId, Is.EqualTo("ent_b"));
        }
    }
}
