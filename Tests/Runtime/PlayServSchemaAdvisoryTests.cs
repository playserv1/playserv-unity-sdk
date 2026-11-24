using System;
using System.Threading;
using NUnit.Framework;
using Playserv.Data;
using Playserv.Serialization;
using Playserv.Schema;
using Playserv.Wrapper;
using UnityEngine;

namespace Playserv.Tests.Runtime
{
    public sealed partial class PlayServTypedRecordsTests
    {
        [PlayServSchema(Singleton = true)]
        private sealed class AdvisorySingleton { }

        [Test]
        public void SchemaAdvisoryReportsVisibleSingletonMismatch()
        {
            var http = new FakeDataHttpClient();
            http.EnqueueCatalogueJson("{\"data\":[{\"entity_id\":\"ent_settings\",\"name\":\"AdvisorySingleton\",\"singleton\":false}]}");
            var client = new PlayServRecordsClient(Settings(), http, new NewtonsoftJsonCodec());
            var result = client.CheckSchemaAsync(new[] { typeof(AdvisorySingleton) }, default).GetAwaiter().GetResult()[0];
            Assert.That(result.Availability, Is.EqualTo(PlayServSchemaAvailability.Visible));
            Assert.That(result.Reason, Is.EqualTo("singleton_mismatch"));
        }

        [Test]
        public void SchemaAdvisoryDoesNotInventCompatibilityWhenKindMetadataIsMissing()
        {
            var http = new FakeDataHttpClient();
            http.EnqueueCatalogueJson("{\"data\":[{\"entity_id\":\"ent_settings\",\"name\":\"AdvisorySingleton\"}]}");
            var client = new PlayServRecordsClient(Settings(), http, new NewtonsoftJsonCodec());
            var result = client.CheckSchemaAsync(new[] { typeof(AdvisorySingleton) }, default).GetAwaiter().GetResult()[0];
            Assert.That(result.Compatibility, Is.EqualTo(PlayServSchemaCompatibility.Unknown));
            Assert.That(result.Reason, Is.EqualTo("singleton_metadata_unavailable"));
        }

        [Test]
        public void SchemaAdvisoryOptInSurvivesConfigAndSettingsRoundTrip()
        {
            var property = typeof(PlayServSettings).GetProperty("EnableAutomaticSchemaAdvisory");
            Assert.That(property, Is.Not.Null, "Automatic schema advisory must have an explicit default-off setting.");
            var settings = Settings();
            Assert.That(property.GetValue(settings), Is.False);
            property.SetValue(settings, true);
            var config = ScriptableObject.CreateInstance<PlayServConfig>();
            try
            {
                config.ApplySettings(settings);
                Assert.That(property.GetValue(settings.Clone()), Is.True);
                Assert.That(property.GetValue(config.ToSettings()), Is.True);
            }
            finally { UnityEngine.Object.DestroyImmediate(config); }
        }

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
