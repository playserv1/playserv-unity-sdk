using System;
using System.Collections.Generic;
using NUnit.Framework;
using Playserv.Proxy.Common;
using Playserv.Serialization;
using Playserv.Wrapper;

namespace Playserv.Tests.Runtime
{
    public sealed class PlayServSerializationTests
    {
        [Serializable]
        private sealed class Payload
        {
            public string Name;
            public int Count;
            public NestedPayload Nested;
            public string Optional;
        }

        [Serializable]
        private sealed class NestedPayload
        {
            public bool Enabled;
        }

        [Test]
        public void NewtonsoftCodec_RoundTripsNestedPayload()
        {
            var codec = new NewtonsoftJsonCodec();
            var source = new Payload
            {
                Name = "alpha",
                Count = 7,
                Nested = new NestedPayload { Enabled = true }
            };

            var json = codec.Serialize(
                source,
                new JsonCodecOptions { IncludeNullValues = false });
            var result = codec.Deserialize<Payload>(json);

            Assert.That(result.Name, Is.EqualTo(source.Name));
            Assert.That(result.Count, Is.EqualTo(source.Count));
            Assert.That(result.Nested.Enabled, Is.True);
            Assert.That(json, Does.Not.Contain("Optional"));
        }

        [Test]
        public void NewtonsoftCodec_ConvertsJsonToPlainValues()
        {
            var codec = new NewtonsoftJsonCodec();

            var result = (Dictionary<string, object>)codec.ParseToPlainValue(
                "{\"name\":\"alpha\",\"items\":[1,true,null]}");

            Assert.That(result["name"], Is.EqualTo("alpha"));
            Assert.That(result["items"], Is.TypeOf<List<object>>());
            Assert.That((List<object>)result["items"], Has.Count.EqualTo(3));
        }

        [Test]
        public void NewtonsoftCodec_InvalidJson_ThrowsCodecException()
        {
            var codec = new NewtonsoftJsonCodec();

            Assert.Throws<JsonCodecException>(() => codec.Deserialize<Payload>("{"));
        }

        [Test]
        public void HandshakeRequest_DoesNotSerializeLegacyGameOrUserIdentity()
        {
            var codec = new NewtonsoftJsonCodec();
            var request = new HandshakeRequest
            {
                ClientToken = "pk_project",
                Authorization = "Bearer player-jwt",
                GameVersion = "1.2.3",
                SdkVersion = "0.5.1"
            };

            var json = codec.Serialize(request);

            Assert.That(json, Does.Contain("pk_project"));
            Assert.That(json, Does.Contain("player-jwt"));
            Assert.That(json, Does.Not.Contain("GameId"));
            Assert.That(json, Does.Not.Contain("UserId"));
        }

        [Test]
        public void RuntimeSettings_DoNotExposeCallerSuppliedGameOrUserIdentity()
        {
            Assert.That(typeof(PlayServSettings).GetProperty("GameId"), Is.Null);
            Assert.That(typeof(PlayServSettings).GetProperty("UserId"), Is.Null);
            Assert.That(typeof(PlayServSettings).GetProperty("DeploymentGameId"), Is.Not.Null);
        }
    }
}
