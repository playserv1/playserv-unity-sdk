using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Playserv.Data;
using Playserv.Schema;
using Playserv.Serialization;

namespace Playserv.Tests.Runtime
{
    public sealed class PlayServGeneratedSerializationTests
    {
        public sealed class Nested { public string Text; }
        public sealed class Payload
        {
            [PlayServJsonName("wire_name")] public string Name;
            public int Count { get; set; }
            public Nested Child;
            public List<int?> Numbers;
            public Dictionary<string, Nested> Map;
            public string Optional;
            [JsonIgnore] public string Local = "kept";
        }
        private sealed class Late { public int Value; }
        private sealed class LateChild { public int Value; }
        private sealed class LateParent { public LateChild Child; }
        private sealed class GeneratedParent { public GeneratedChild Child; }
        private sealed class GeneratedChild { public int Value; }
        private sealed class Fallback
        {
            [JsonProperty("hidden")] private string _hidden = "private survives";
            public string Visible = "visible";
            public string Hidden => _hidden;
        }

        private static int _reads;
        private static int _writes;

        [SetUp]
        public void RegisterFixture()
        {
            _reads = _writes = 0;
            PlayServGeneratedContracts.Register(() => new Nested(),
                PlayServGeneratedProperty.Create<Nested, string>("Text", "Text", x => x.Text, (x, v) => x.Text = v));
            PlayServGeneratedContracts.Register(() => new Payload(),
                PlayServGeneratedProperty.Create<Payload, string>("wire_name", "Name", x => { _reads++; return x.Name; }, (x, v) => { _writes++; x.Name = v; }),
                PlayServGeneratedProperty.Create<Payload, int>("Count", "Count", x => x.Count, (x, v) => x.Count = v),
                PlayServGeneratedProperty.Create<Payload, Nested>("Child", "Child", x => x.Child, (x, v) => x.Child = v),
                PlayServGeneratedProperty.Create<Payload, List<int?>>("Numbers", "Numbers", x => x.Numbers, (x, v) => x.Numbers = v),
                PlayServGeneratedProperty.Create<Payload, Dictionary<string, Nested>>("Map", "Map", x => x.Map, (x, v) => x.Map = v),
                PlayServGeneratedProperty.Create<Payload, string>("Optional", "Optional", x => x.Optional, (x, v) => x.Optional = v),
                PlayServGeneratedProperty.Ignore<Payload>("Local", "Local"));
        }

        [Test]
        public void GeneratedContractsCanBeRegisteredAndSelectedWithoutReflectionFallback()
        {
            var codec = new NewtonsoftJsonCodec();
            Assert.That(PlayServGeneratedContracts.IsRegistered(typeof(Payload)), Is.True);
            var result = codec.Deserialize<Payload>(codec.Serialize(new Payload { Name = "generated" }));
            Assert.That(result.Name, Is.EqualTo("generated"));
            Assert.That(_reads, Is.GreaterThan(0), "A registry entry alone does not prove the codec uses generated accessors.");
            Assert.That(_writes, Is.GreaterThan(0));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void GeneratedAndCachedReflectionPathsProduceEquivalentWireAndRoundTrip(bool includeNull)
        {
            var source = new Payload { Name = "Юнікод", Count = 7, Child = new Nested { Text = "nested" },
                Numbers = new List<int?> { 1, null, 3 }, Map = new Dictionary<string, Nested> { ["key"] = new Nested { Text = "value" } } };
            var options = new JsonCodecOptions { IncludeNullValues = includeNull };
            var generated = new NewtonsoftJsonCodec();
            var reflection = new NewtonsoftJsonCodec(false);
            var expected = reflection.Serialize(source, options);
            var actual = generated.Serialize(source, options);
            Assert.That(JToken.DeepEquals(JToken.Parse(actual), JToken.Parse(expected)), Is.True, actual);
            Assert.That(reflection.Serialize(generated.Deserialize<Payload>(expected, options)),
                Is.EqualTo(reflection.Serialize(reflection.Deserialize<Payload>(expected, options))));
            Assert.That(generated.Deserialize<Payload>(actual).Local, Is.EqualTo("kept"));
        }

        [Test]
        public void IgnoredIncomingMembersRemainIgnoredEvenWhenUnknownFieldsAreErrors()
        {
            var options = new JsonCodecOptions { IgnoreMissingMembers = false };
            Assert.That(new NewtonsoftJsonCodec().Deserialize<Payload>("{\"Local\":\"not persisted\"}", options).Local, Is.EqualTo("kept"));
            Assert.Throws<JsonCodecException>(() => new NewtonsoftJsonCodec().Deserialize<Payload>("{\"unknown\":1}", options));
        }

        [Test]
        public void RegistrationAfterFirstUseAndRepeatedRegistrationDoNotLeaveACachedFallback()
        {
            var codec = new NewtonsoftJsonCodec();
            codec.Serialize(new Late());
            var reads = 0;
            Action register = () => PlayServGeneratedContracts.Register(() => new Late(),
                PlayServGeneratedProperty.Create<Late, int>("Value", "Value", x => { reads++; return x.Value; }, (x, v) => x.Value = v));
            register(); codec.Serialize(new Late { Value = 2 });
            register(); codec.Serialize(new Late { Value = 3 });
            Assert.That(reads, Is.EqualTo(2));
        }

        [Test]
        public void LateRegistrationReplacesContractsCachedInsideParentsArraysAndDictionaries()
        {
            var codec = new NewtonsoftJsonCodec();
            var child = new LateChild { Value = 7 };
            var parent = new LateParent { Child = child };
            var array = new[] { child };
            var dictionary = new Dictionary<string, LateChild> { ["child"] = child };
            codec.Serialize(parent); codec.Serialize(array); codec.Serialize(dictionary);
            var reads = 0;
            PlayServGeneratedContracts.Register(() => new LateChild(),
                PlayServGeneratedProperty.Create<LateChild, int>("Value", "Value", x => { reads++; return x.Value; }, (x, v) => x.Value = v));
            codec.Serialize(parent); codec.Serialize(array); codec.Serialize(dictionary);
            Assert.That(reads, Is.EqualTo(3), "Nested Newtonsoft contract caches must not bypass late registration.");
            var replacementReads = 0;
            PlayServGeneratedContracts.Register(() => new LateChild(),
                PlayServGeneratedProperty.Create<LateChild, int>("Value", "Value", x => { replacementReads++; return x.Value; }, (x, v) => x.Value = v));
            codec.Serialize(parent); codec.Serialize(array); codec.Serialize(dictionary);
            Assert.That(replacementReads, Is.EqualTo(3));
        }

        [Test]
        public void LateChildRegistrationAlsoRebuildsAnAlreadyGeneratedParentContract()
        {
            var codec = new NewtonsoftJsonCodec();
            PlayServGeneratedContracts.Register(() => new GeneratedParent(),
                PlayServGeneratedProperty.Create<GeneratedParent, GeneratedChild>("Child", "Child", x => x.Child, (x, v) => x.Child = v));
            var parent = new GeneratedParent { Child = new GeneratedChild { Value = 9 } };
            codec.Serialize(parent);
            var reads = 0;
            PlayServGeneratedContracts.Register(() => new GeneratedChild(),
                PlayServGeneratedProperty.Create<GeneratedChild, int>("Value", "Value", x => { reads++; return x.Value; }, (x, v) => x.Value = v));
            codec.Serialize(parent);
            Assert.That(reads, Is.EqualTo(1));
        }

        [Test]
        public void FailedRegistrationLeavesThePreviousCompleteContractUsable()
        {
            var property = PlayServGeneratedProperty.Create<Payload, string>("duplicate", "Name", x => x.Name, (x, v) => x.Name = v);
            Assert.Throws<ArgumentException>(() => PlayServGeneratedContracts.Register(() => new Payload(), property, property));
            Assert.That(new NewtonsoftJsonCodec().Serialize(new Payload { Name = "still present" }), Does.Contain("wire_name"));
        }

        [Test]
        public void UnregisteredTypesRetainPrivateAttributedAndReadOnlyMembers()
        {
            var codec = new NewtonsoftJsonCodec();
            Assert.That(PlayServGeneratedContracts.IsRegistered(typeof(Fallback)), Is.False);
            Assert.That(codec.Serialize(new Fallback()), Is.EqualTo(new NewtonsoftJsonCodec(false).Serialize(new Fallback())));
            Assert.That(codec.Deserialize<Fallback>("{\"hidden\":\"restored\"}").Hidden, Is.EqualTo("restored"));
        }

        [Test]
        public void GeneratedContractsPreserveStrictResponseValidation()
        {
            var codec = new NewtonsoftJsonCodec();
            Assert.That(codec.DeserializeStrict<Payload>("{\"wire_name\":\"strict\",\"Count\":2}").Name, Is.EqualTo("strict"));
            Assert.That(_writes, Is.GreaterThan(0));
            Assert.Throws<PlayServStrictResponseException>(() => codec.DeserializeStrict<Payload>("{\"Count\":\"wrong\"}"));
        }

    }

    public sealed partial class PlayServTypedRecordsTests
    {
        private sealed class RefPayload { public PlayServRecordRef<RefPayload> Child; public PlayServRecordRef<RefPayload>[] Children; }

        [Test]
        public void RecordReferencesStillUseTheContextConverterForIdsExpandedValuesAndNull()
        {
            PlayServGeneratedContracts.Register(() => new RefPayload(),
                PlayServGeneratedProperty.Create<RefPayload, PlayServRecordRef<RefPayload>>("Child", "Child", x => x.Child, (x, v) => x.Child = v),
                PlayServGeneratedProperty.Create<RefPayload, PlayServRecordRef<RefPayload>[]>("Children", "Children", x => x.Children, (x, v) => x.Children = v));
            var codec = new NewtonsoftJsonCodec();
            var client = new PlayServRecordsClient(Settings(), ReferenceHttp(), codec);
            var options = new JsonCodecOptions { CustomConverters = new object[] { new PlayServRecordRefConverter(client) } };
            var value = codec.Deserialize<RefPayload>("{\"Child\":{\"id\":\"rec_child\",\"Child\":null},\"Children\":[\"rec_one\",null]}", options);
            Assert.That(value.Child.Id, Is.EqualTo("rec_child"));
            Assert.That(value.Child.State, Is.EqualTo(PlayServRecordRefState.Expanded));
            Assert.That(value.Children[0].Id, Is.EqualTo("rec_one"));
            Assert.That(value.Children[1], Is.Null);
            var json = codec.Serialize(value, options);
            Assert.That(json, Is.EqualTo(new NewtonsoftJsonCodec(false).Serialize(value, options)));
            Assert.That(json, Is.EqualTo("{\"Child\":\"rec_child\",\"Children\":[\"rec_one\",null]}"));
        }
    }
}
