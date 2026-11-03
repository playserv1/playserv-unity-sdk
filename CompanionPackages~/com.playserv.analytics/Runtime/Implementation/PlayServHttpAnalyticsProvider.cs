using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Http.Common;
using Playserv.Http.Interfaces;
using Playserv.Runtime.Abstractions;
using Playserv.Serialization;
using Playserv.Wrapper;

namespace Playserv.Analytics
{
    /// <summary>
    /// Default analytics provider backed by the runtime
    /// <c>POST /analytics/events</c> ingestion endpoint.
    /// </summary>
    internal sealed class PlayServHttpAnalyticsProvider : IPlayServAnalyticsProvider
    {
        internal const string EventsPath = "analytics/events";
        internal const string Channel = "unity";

        private readonly PlayServSettings _settings;
        private readonly IPlayServRuntimeHttpClient _http;
        private readonly IJsonCodec _json;

        internal PlayServHttpAnalyticsProvider(
            PlayServSettings settings,
            IPlayServRuntimeHttpClient http,
            IJsonCodec json)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _json = json ?? throw new ArgumentNullException(nameof(json));
        }

        internal static PlayServHttpAnalyticsProvider CreateDefault()
        {
            var settings = PlayServ.Settings;
            var json = new NewtonsoftJsonCodec();
            var http = PlayServRuntimeHttpClientResolver.Create(
                new PlayServHttpModuleContext(settings.ToRuntimeSettings(), json));
            return new PlayServHttpAnalyticsProvider(settings, http, json);
        }

        public bool IsReady =>
            !string.IsNullOrWhiteSpace(_settings.BackendServerAddress) &&
            !string.IsNullOrWhiteSpace(_settings.ClientToken);

        public async Task SendAsync(
            PlayServAnalyticsBatch batch,
            CancellationToken cancellationToken = default)
        {
            if (batch == null)
                throw new ArgumentNullException(nameof(batch));

            cancellationToken.ThrowIfCancellationRequested();
            if (!IsReady)
            {
                throw new InvalidOperationException(
                    "PlayServ analytics requires BackendServerAddress and a public pk_* ClientToken.");
            }

            var bearer = await ResolvePlayerTokenAsync(cancellationToken);
            var body = SerializeBatch(_json, batch);

            await _http.SendDataAsync(new PlayServRuntimeDataRequest
            {
                Method = "POST",
                RelativePath = EventsPath,
                ClientToken = _settings.ClientToken,
                BearerToken = bearer,
                JsonBody = body
            }, cancellationToken);
        }

        internal static string SerializeBatch(
            IJsonCodec json,
            PlayServAnalyticsBatch batch)
        {
            if (json == null)
                throw new ArgumentNullException(nameof(json));
            if (batch == null)
                throw new ArgumentNullException(nameof(batch));

            return json.Serialize(new AnalyticsIngestRequestWire
            {
                events = (batch.Events ?? Array.Empty<PlayServAnalyticsEvent>())
                    .Where(value => value != null)
                    .Select(value => ToWireEvent(batch, value))
                    .ToArray()
            });
        }

        private async Task<string> ResolvePlayerTokenAsync(CancellationToken cancellationToken)
        {
            if (_settings.RuntimeTokenProvider != null)
                return await _settings.RuntimeTokenProvider.GetTokenAsync(cancellationToken);

            return string.IsNullOrWhiteSpace(_settings.PlayerAccessToken)
                ? null
                : _settings.PlayerAccessToken;
        }

        private static AnalyticsIngestEventWire ToWireEvent(
            PlayServAnalyticsBatch batch,
            PlayServAnalyticsEvent source)
        {
            var parameters = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var parameter in source.Parameters ?? Array.Empty<PlayServAnalyticsParameter>())
            {
                if (parameter == null || string.IsNullOrWhiteSpace(parameter.Key))
                    continue;
                parameters[parameter.Key] = ToParameterValue(parameter);
            }

            var userProperties = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var property in source.UserProperties ?? Array.Empty<PlayServAnalyticsUserProperty>())
            {
                if (property == null || string.IsNullOrWhiteSpace(property.Key))
                    continue;
                userProperties[property.Key] = property.Value ?? string.Empty;
            }

            return new AnalyticsIngestEventWire
            {
                type = source.Name,
                channel = Channel,
                event_time = DateTimeOffset
                    .FromUnixTimeMilliseconds(source.TimestampUnixMilliseconds)
                    .UtcDateTime
                    .ToString("O", CultureInfo.InvariantCulture),
                payload = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["event_id"] = source.EventId ?? string.Empty,
                    ["sequence"] = source.Sequence,
                    ["session_id"] = source.SessionId ?? string.Empty,
                    ["user_id"] = source.UserId ?? string.Empty,
                    ["sdk_version"] = source.SdkVersion ?? string.Empty,
                    ["application_version"] = source.ApplicationVersion ?? string.Empty,
                    ["platform"] = source.Platform ?? string.Empty,
                    ["batch_id"] = batch.BatchId ?? string.Empty,
                    ["batch_sent_at_unix_ms"] = batch.SentAtUnixMilliseconds,
                    ["parameters"] = parameters,
                    ["user_properties"] = userProperties
                }
            };
        }

        private static object ToParameterValue(PlayServAnalyticsParameter parameter)
        {
            switch (parameter.Type)
            {
                case PlayServAnalyticsParameterType.Integer:
                    return parameter.IntegerValue;
                case PlayServAnalyticsParameterType.Number:
                    return parameter.NumberValue;
                case PlayServAnalyticsParameterType.Boolean:
                    return parameter.BooleanValue;
                default:
                    return parameter.StringValue ?? string.Empty;
            }
        }

        [Serializable]
        private sealed class AnalyticsIngestRequestWire
        {
            public AnalyticsIngestEventWire[] events;
        }

        [Serializable]
        private sealed class AnalyticsIngestEventWire
        {
            public string type;
            public string channel;
            public string event_time;
            public Dictionary<string, object> payload;
        }
    }
}
