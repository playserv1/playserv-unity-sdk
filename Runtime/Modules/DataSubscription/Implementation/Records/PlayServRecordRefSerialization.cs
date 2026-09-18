using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Playserv.Serialization;
using Playserv.Wrapper;

namespace Playserv.Data
{
    internal sealed class PlayServRecordRefConverter : JsonConverter
    {
        private readonly PlayServRecordsClient _client;
        private int _previewDepth;
        internal readonly Dictionary<string, string> ExpandedPaths = new Dictionary<string, string>();
        internal PlayServRecordRefConverter(PlayServRecordsClient client) { _client = client; }
        public override bool CanConvert(Type type) => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(PlayServRecordRef<>);
        public override object ReadJson(JsonReader reader, Type type, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;
            var path = reader.Path;
            var token = JToken.Load(reader);
            var expanded = token as JObject;
            var idToken = expanded == null ? token : expanded["id"];
            if (idToken?.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)idToken))
                throw new JsonSerializationException("A record reference must be an ID, null, or an expanded object with an ID.");
            var id = (string)idToken;
            var reference = (IPlayServRecordRef)Activator.CreateInstance(type, id);
            if (expanded == null) reference.Bind(_client, null, serializer);
            else
            {
                _previewDepth++;
                try { reference.Bind(_client, expanded, serializer); }
                finally { _previewDepth--; }
                // Nested previews are already collapsed by the containing relation ID.
                if (_previewDepth == 0) ExpandedPaths[path] = id;
            }
            return reference;
        }
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value == null) { writer.WriteNull(); return; }
            var reference = (IPlayServRecordRef)value;
            reference.ValidateContext();
            writer.WriteValue(reference.Id);
        }
    }

    internal sealed partial class PlayServRecordsClient
    {
        private Action _validateReferenceScope;
        private bool _isReferenceClient;
        private PlayServRecordsClient _referenceClient;

        private void CaptureReferenceContext(Func<bool> isCurrent)
        {
            var address = _settings.BackendServerAddress;
            var token = _settings.ClientToken;
            var player = _settings.PlayerId;
            var accessToken = _settings.PlayerAccessToken;
            var provider = _settings.RuntimeTokenProvider;
            var managed = provider as PlayServPlayerSession;
            var revision = managed?.IdentityRevision;
            var managedPlayer = managed?.PlayerId;
            _validateReferenceScope = () =>
            {
                bool externalCurrent;
                try { externalCurrent = isCurrent == null || isCurrent(); } catch { externalCurrent = false; }
                if (!externalCurrent || address != _settings.BackendServerAddress || token != _settings.ClientToken ||
                    player != _settings.PlayerId || !ReferenceEquals(provider, _settings.RuntimeTokenProvider) ||
                    (provider == null && accessToken != _settings.PlayerAccessToken) ||
                    (managed != null && (revision != managed.IdentityRevision || managedPlayer != managed.PlayerId)))
                    throw new InvalidOperationException("The record reference belongs to a previous authorization context. Obtain a new Records<T>() context.");
            };
        }
        internal void ValidateReferenceContext() => _validateReferenceScope();
        internal PlayServRecordsClient ForReferences()
        {
            if (_isReferenceClient) return this;
            if (_referenceClient != null) return _referenceClient;
            _referenceClient = new PlayServRecordsClient(_settings, _http, _json, _requiresClientToken, _accessSubject, _catalogueCacheScope)
            { _isReferenceClient = true, _validateReferenceScope = _validateReferenceScope };
            return _referenceClient;
        }
        private JsonCodecOptions ReferenceJsonOptions(PlayServRecordRefConverter converter = null) => new JsonCodecOptions
        { CustomConverters = new object[] { converter ?? new PlayServRecordRefConverter(this) } };
        internal T ConvertRecordValue<T>(object fields) => _json.Convert<T>(fields, ReferenceJsonOptions());
        internal T CloneRecordValue<T>(T value) => ConvertRecordValue<T>(ToRecordFields(value));
        private T ParseReferenceFields<T>(Dictionary<string, object> fields, out string canonical)
        {
            var converter = new PlayServRecordRefConverter(this);
            var value = _json.Convert<T>(fields, ReferenceJsonOptions(converter));
            if (converter.ExpandedPaths.Count == 0) canonical = Canonical(fields);
            else
            {
                var root = JObject.Parse(_json.Serialize(fields));
                foreach (var entry in converter.ExpandedPaths.OrderByDescending(p => p.Key.Length))
                    root.SelectToken(entry.Key, false)?.Replace(new JValue(entry.Value));
                canonical = Canonical(_json.ParseToPlainValue(root.ToString(Formatting.None)));
            }
            return value;
        }
    }
}
