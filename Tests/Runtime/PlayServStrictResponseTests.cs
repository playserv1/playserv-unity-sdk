using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using NUnit.Framework;
using Playserv.Code;
using Playserv.Http.Interfaces;
using Playserv.Runtime.Abstractions;
using Playserv.Serialization;
using Playserv.Wrapper;
using UnityEngine.TestTools;

namespace Playserv.Tests.Runtime
{
    public sealed class PlayServStrictResponseTests
    {
        private readonly IPlayServStrictJsonCodec _codec = new NewtonsoftJsonCodec();

        [TestCase("\"7\"")]
        [TestCase("true")]
        [TestCase("{}")]
        [TestCase("[]")]
        [TestCase("null")]
        [TestCase("1.5")]
        [TestCase("2147483648")]
        [TestCase("-2147483649")]
        [TestCase("")]
        [TestCase("{secret")]
        [TestCase("7 8")]
        public void Integer_rejects_coercion_overflow_and_invalid_json(string json) =>
            Assert.Throws<PlayServStrictResponseException>(() => _codec.DeserializeStrict<int>(json));

        [Test]
        public void Scalars_and_numeric_boundaries_round_trip()
        {
            Assert.That(_codec.DeserializeStrict<int>("2147483647"), Is.EqualTo(int.MaxValue));
            Assert.That(_codec.DeserializeStrict<long>("-9223372036854775808"), Is.EqualTo(long.MinValue));
            Assert.That(_codec.DeserializeStrict<ulong>("18446744073709551615"), Is.EqualTo(ulong.MaxValue));
            Assert.Throws<PlayServStrictResponseException>(() => _codec.DeserializeStrict<ulong>("18446744073709551616"));
            Assert.Throws<PlayServStrictResponseException>(() => _codec.DeserializeStrict<long>("9223372036854775806.5"));
            Assert.Throws<PlayServStrictResponseException>(() => _codec.DeserializeStrict<byte>("-1"));
            Assert.That(_codec.DeserializeStrict<double>("1.25e100"), Is.EqualTo(1.25e100));
            Assert.Throws<PlayServStrictResponseException>(() => _codec.DeserializeStrict<float>("1e100"));
            Assert.That(_codec.DeserializeStrict<decimal>("1.25"), Is.EqualTo(1.25m));
            Assert.That(_codec.DeserializeStrict<decimal>("1.234567890123456789"), Is.EqualTo(1.234567890123456789m));
            Assert.That(_codec.DeserializeStrict<bool>("true"), Is.True);
            Assert.Throws<PlayServStrictResponseException>(() => _codec.DeserializeStrict<bool>("1"));
            Assert.That(_codec.DeserializeStrict<string>("\"text\""), Is.EqualTo("text"));
            Assert.Throws<PlayServStrictResponseException>(() => _codec.DeserializeStrict<string>("42"));
            Assert.That(_codec.DeserializeStrict<int?>("null"), Is.Null);
            Assert.That(_codec.DeserializeStrict<Container>("null"), Is.Null);
        }

        [Test]
        public void Resolver_names_ignored_required_unknown_nested_and_dictionary_values_are_checked()
        {
            var dto = _codec.DeserializeStrict<Container>("{\"wire\":1,\"Ignored\":{},\"extra\":true,\"Rows\":[{\"Count\":2}],\"Lookup\":{\"key\":3}}");
            Assert.That(dto.Named, Is.EqualTo(1));
            Assert.That(dto.Rows[0].Count, Is.EqualTo(2));
            Assert.Throws<PlayServStrictResponseException>(() => _codec.DeserializeStrict<Container>("{}"));
            Assert.Throws<PlayServStrictResponseException>(() => _codec.DeserializeStrict<Container>("{\"wire\":null}"));
            var nested = Assert.Throws<PlayServStrictResponseException>(() => _codec.DeserializeStrict<Container>(
                "{\"wire\":1,\"Rows\":[{\"Count\":\"private-value\"}]}"));
            Assert.That(nested.Path, Is.EqualTo("$.Rows[0].Count"));
            Assert.That(nested.ToString(), Does.Not.Contain("private-value"));
            var dictionary = Assert.Throws<PlayServStrictResponseException>(() => _codec.DeserializeStrict<Container>(
                "{\"wire\":1,\"Lookup\":{\"private-key\":\"private-value\"}}"));
            Assert.That(dictionary.Path, Is.EqualTo("$.Lookup[*]"));
            Assert.That(dictionary.ToString(), Does.Not.Contain("private-"));
            Assert.Throws<PlayServStrictResponseException>(() => _codec.DeserializeStrict<Row>("{\"Count\":1,\"count\":\"2\"}"));
        }

        [Test]
        public void Enum_formats_follow_the_contract_converter()
        {
            Assert.That(_codec.DeserializeStrict<NumericEnum>("1"), Is.EqualTo(NumericEnum.One));
            Assert.Throws<PlayServStrictResponseException>(() => _codec.DeserializeStrict<NumericEnum>("\"One\""));
            Assert.That(_codec.DeserializeStrict<Enums>("{\"Value\":\"One\"}").Value, Is.EqualTo(NumericEnum.One));
            var error = Assert.Throws<PlayServStrictResponseException>(() => _codec.DeserializeStrict<Enums>("{\"Value\":\"private-enum\"}"));
            Assert.That(error.Path, Is.EqualTo("$.Value"));
            Assert.That(error.ToString(), Does.Not.Contain("private-enum"));
        }

        [Test]
        public void Special_types_require_valid_strings()
        {
            Assert.That(_codec.DeserializeStrict<Guid>("\"97ce1dde-6627-4546-85f7-780bd52d082a\""), Is.Not.EqualTo(Guid.Empty));
            Assert.That(_codec.DeserializeStrict<DateTimeOffset>("\"2026-09-07T00:00:00Z\"").Year, Is.EqualTo(2026));
            Assert.That(_codec.DeserializeStrict<TimeSpan>("\"00:00:01\""), Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(_codec.DeserializeStrict<byte[]>("\"AQI=\""), Is.EqualTo(new byte[] { 1, 2 }));
            Assert.Throws<PlayServStrictResponseException>(() => _codec.DeserializeStrict<byte[]>("[1,2]"));
            Assert.Throws<PlayServStrictResponseException>(() => _codec.DeserializeStrict<Guid>("\"private-guid\""));
        }

        [Test]
        public void Unsupported_nested_converters_and_contracts_fail_preflight()
        {
            Assert.Throws<PlayServStrictResponseException>(() => _codec.ValidateResponseType(typeof(Converted)));
            Assert.Throws<PlayServStrictResponseException>(() => _codec.ValidateResponseType(typeof(Dictionary<int, Row>)));
            Assert.Throws<PlayServStrictResponseException>(() => _codec.ValidateResponseType(typeof(IntPtr)));
            Assert.Throws<PlayServStrictResponseException>(() => _codec.ValidateResponseType(typeof(int[,])));
            _codec.ValidateResponseType(typeof(Recursive));
        }

        public sealed class Row { public int Count { get; set; } }
        public sealed class Recursive { public Recursive Child { get; set; } }
        public sealed class Container
        {
            [PlayServJsonName("wire"), JsonProperty(Required = Required.Always)] public int Named;
            [JsonIgnore] public int Ignored;
            public List<Row> Rows;
            public Dictionary<string, int> Lookup;
        }
        public enum NumericEnum : byte { Zero, One }
        public sealed class Enums { [JsonConverter(typeof(StringEnumConverter))] public NumericEnum Value; }
        public sealed class Converted { [JsonConverter(typeof(UnsafeConverter))] public Row Value; }
        public sealed class UnsafeConverter : JsonConverter
        {
            public override bool CanConvert(Type type) => true;
            public override object ReadJson(JsonReader reader, Type type, object existing, JsonSerializer serializer) => new Row();
            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) => throw new NotSupportedException();
        }
    }

    public sealed partial class PlayServCodeTests
    {
        [TestCase(false, true)]
        [TestCase(true, false)]
        public void Typed_code_strict_flag_controls_coercion(bool strict, bool success)
        {
            var http = new FakeRuntimeHttpClient();
            http.Enqueue(new PlayServRuntimeDataResponse(200, "{\"value\":\"42\"}", null, null, "application/json", null));
            var result = CreateClient(http).CallAsync<ValueResponse>("echo", null,
                new PlayServFunctionCallOptions { StrictResponseTypes = strict }, default).GetAwaiter().GetResult();
            Assert.That(result.IsSuccess, Is.EqualTo(success));
            if (strict)
            {
                Assert.That(result.Error.Code, Is.EqualTo(PlayServErrorCode.Deserialization));
                Assert.That(result.Error.Message, Does.Contain("$.value").And.Not.Contain("42"));
                Assert.That(result.Error.RawDetails, Is.Null.Or.Empty);
            }
            else Assert.That(result.Value.value, Is.EqualTo(42));
        }

        [TestCase("", false)]
        [TestCase("plain-text", false)]
        [TestCase("\"json-text\"", true)]
        public void Strict_code_string_requires_json(string body, bool success)
        {
            var http = new FakeRuntimeHttpClient();
            http.Enqueue(new PlayServRuntimeDataResponse(200, body, null, null, "application/json", null));
            var result = CreateClient(http).CallAsync<string>("echo", null,
                new PlayServFunctionCallOptions { StrictResponseTypes = true }, default).GetAwaiter().GetResult();
            Assert.That(result.IsSuccess, Is.EqualTo(success));
            if (success) Assert.That(result.Value, Is.EqualTo("json-text"));
        }

        [Test]
        public void Strict_code_unsupported_contract_or_codec_fails_before_io()
        {
            var http = new FakeRuntimeHttpClient();
            var options = new PlayServFunctionCallOptions { StrictResponseTypes = true };
            var result = CreateClient(http).CallAsync<PlayServStrictResponseTests.Converted>("echo", null, options, default).GetAwaiter().GetResult();
            Assert.That(result.Error.Code, Is.EqualTo(PlayServErrorCode.Deserialization));
            var unsupported = CreateClient(http, codec: new LegacyCodec()).CallAsync<int>("echo", null, options, default).GetAwaiter().GetResult();
            Assert.That(unsupported.Error.Code, Is.EqualTo(PlayServErrorCode.Deserialization));
            Assert.That(http.Requests, Is.Empty);
            using var ct = new CancellationTokenSource();
            ct.Cancel();
            Assert.Throws<OperationCanceledException>(() => CreateClient(http).CallAsync<int>("echo", null, options, ct.Token).GetAwaiter().GetResult());
            Assert.That(http.Requests, Is.Empty);
        }

        [UnityTest]
        public IEnumerator Strict_code_options_are_captured_per_call()
        {
            var pending = new TaskCompletionSource<PlayServRuntimeDataResponse>();
            var http = new FakeRuntimeHttpClient { Handler = _ => pending.Task };
            var options = new PlayServFunctionCallOptions { StrictResponseTypes = true };
            var client = CreateClient(http);
            var strict = client.CallAsync<ValueResponse>("echo", null, options, default);
            options.StrictResponseTypes = false;
            var compatible = client.CallAsync<ValueResponse>("echo", null, options, default);
            pending.SetResult(new PlayServRuntimeDataResponse(200, "{\"value\":\"42\"}", null, null, "application/json", null));
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while ((!strict.IsCompleted || !compatible.IsCompleted) && DateTime.UtcNow < deadline) yield return null;
            Assert.That(strict.IsCompleted && compatible.IsCompleted, Is.True);
            Assert.That(strict.GetAwaiter().GetResult().IsSuccess, Is.False);
            Assert.That(compatible.GetAwaiter().GetResult().IsSuccess, Is.True);
        }

        private sealed class LegacyCodec : IJsonCodec
        {
            public string Serialize(object value, JsonCodecOptions options = null) => "{}";
            public T Deserialize<T>(string json, JsonCodecOptions options = null) => default;
            public object Deserialize(string json, Type type, JsonCodecOptions options = null) => null;
            public T Convert<T>(object value, JsonCodecOptions options = null) => default;
            public object Convert(object value, Type type, JsonCodecOptions options = null) => null;
            public string ToCanonicalJson(object value, JsonCodecOptions options = null) => "{}";
            public object Clone(object value, JsonCodecOptions options = null) => value;
            public object ToPlainValue(object value, JsonCodecOptions options = null) => value;
            public object ParseToPlainValue(string json, JsonCodecOptions options = null) => null;
            public bool TryGetProperty(object value, string name, bool ignoreCase, out object property) { property = null; return false; }
            public bool TryGetFirstPropertyValue(object value, out object property) { property = null; return false; }
        }
    }
}
