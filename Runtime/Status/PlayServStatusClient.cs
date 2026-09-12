using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Http.Common;
using Playserv.Http.Interfaces;
using Playserv.Runtime.Abstractions;
using Playserv.Serialization;
using Playserv.Wrapper;

namespace Playserv.Status
{
    internal sealed class PlayServStatusClient
    {
        private readonly PlayServSettings _settings;
        private readonly IPlayServRuntimeHttpClient _http;
        private readonly IJsonCodec _json;

        internal PlayServStatusClient(
            PlayServSettings settings,
            IPlayServRuntimeHttpClient http,
            IJsonCodec json)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _json = json ?? throw new ArgumentNullException(nameof(json));
        }

        internal static PlayServStatusClient CreateDefault()
        {
            var settings = PlayServ.Settings;
            var json = new NewtonsoftJsonCodec();
            var http = PlayServRuntimeHttpClientResolver.Create(
                new PlayServHttpModuleContext(settings.ToRuntimeSettings(), json));
            return new PlayServStatusClient(settings, http, json);
        }

        internal Task<PlayServPlatformStatus> GetCurrentAsync(CancellationToken cancellationToken) =>
            GetAsync<PlayServPlatformStatus>("status", cancellationToken);

        internal async Task<PlayServProjectStatus> GetProjectAsync(
            string projectSlug, string environment, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(projectSlug) || projectSlug.Any(char.IsControl) ||
                projectSlug.Trim() == "." || projectSlug.Trim() == "..")
                throw new ArgumentException("A project slug without control characters is required.", nameof(projectSlug));
            if (environment != null && environment.Any(char.IsControl))
                throw new ArgumentException("Environment cannot contain control characters.", nameof(environment));
            var path = "status/" + Uri.EscapeDataString(projectSlug.Trim());
            if (!string.IsNullOrWhiteSpace(environment))
                path += "?env=" + Uri.EscapeDataString(environment.Trim());
            var result = await GetAsync<PlayServProjectStatus>(path, cancellationToken);
            if (string.IsNullOrWhiteSpace(result.ProjectSlug) || result.GeneratedAt == default || result.Functions == null ||
                result.Functions.Any(function => function == null || string.IsNullOrWhiteSpace(function.FunctionId) ||
                    string.IsNullOrWhiteSpace(function.Slug) || string.IsNullOrWhiteSpace(function.Environment) ||
                    string.IsNullOrWhiteSpace(function.Kind)))
                throw InvalidResponse("status_project_response_invalid", "Project status response is missing required metadata.");
            return result;
        }

        internal Task<PlayServPlatformStatusHistory> GetHistoryAsync(
            string pop,
            int days,
            CancellationToken cancellationToken)
        {
            if (days < 1 || days > 365)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(days),
                    days,
                    "Platform status history must be between 1 and 365 days.");
            }

            var path = "status/history?days=" + days;
            if (!string.IsNullOrWhiteSpace(pop))
                path += "&pop=" + Uri.EscapeDataString(pop.Trim());
            return GetAsync<PlayServPlatformStatusHistory>(path, cancellationToken);
        }

        internal async Task<PlayServStatusFederation> GetFederationAsync(
            CancellationToken cancellationToken)
        {
            var wire = await GetAsync<FederationWire>("status/federation", cancellationToken);
            var source = wire.origins ?? Array.Empty<string>();
            var origins = new List<Uri>(source.Length);
            foreach (var value in source)
            {
                if (!TryNormalizeOrigin(value, out var origin))
                {
                    throw InvalidResponse(
                        "status_federation_origin_invalid",
                        "The platform status federation response contains an invalid HTTP origin.");
                }
                if (!origins.Any(existing => Uri.Compare(
                        existing,
                        origin,
                        UriComponents.AbsoluteUri,
                        UriFormat.SafeUnescaped,
                        StringComparison.OrdinalIgnoreCase) == 0))
                {
                    origins.Add(origin);
                }
            }
            return new PlayServStatusFederation(origins);
        }

        private async Task<T> GetAsync<T>(
            string relativePath,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureConfigured();

            PlayServRuntimeDataResponse response;
            try
            {
                response = await _http.SendDataAsync(new PlayServRuntimeDataRequest
                {
                    Method = "GET",
                    RelativePath = relativePath,
                    RequiresClientToken = false
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (PlayServRuntimeHttpException exception)
            {
                throw new PlayServStatusException(MapHttpError(exception), exception);
            }
            catch (Exception exception)
            {
                throw new PlayServStatusException(
                    new PlayServError(
                        PlayServErrorCode.Network,
                        "status_transport_failed",
                        "The public platform status request failed before a response was received.",
                        retryable: true,
                        rawDetails: exception.Message),
                    exception);
            }

            if (response == null || string.IsNullOrWhiteSpace(response.Body))
            {
                throw InvalidResponse(
                    "status_response_missing",
                    "The public platform status response body is empty.",
                    response?.StatusCode);
            }

            try
            {
                var value = _json.Deserialize<T>(response.Body);
                if (value == null)
                    throw new InvalidOperationException("Deserialized response is null.");
                return value;
            }
            catch (PlayServStatusException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new PlayServStatusException(
                    new PlayServError(
                        PlayServErrorCode.Deserialization,
                        "status_response_deserialization_failed",
                        "The public platform status response could not be deserialized.",
                        httpStatus: response.StatusCode,
                        rawDetails: response.Body),
                    exception);
            }
        }

        private void EnsureConfigured()
        {
            if (string.IsNullOrWhiteSpace(_settings.BackendServerAddress))
            {
                throw new InvalidOperationException(
                    "PlayServ.Settings.BackendServerAddress is required before reading platform status.");
            }
        }

        private static bool TryNormalizeOrigin(string value, out Uri origin)
        {
            origin = null;
            if (string.IsNullOrWhiteSpace(value) ||
                !Uri.TryCreate(value.Trim().TrimEnd('/'), UriKind.Absolute, out var parsed) ||
                (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps) ||
                !string.IsNullOrEmpty(parsed.UserInfo) ||
                !string.IsNullOrEmpty(parsed.Query) ||
                !string.IsNullOrEmpty(parsed.Fragment))
            {
                return false;
            }

            origin = parsed;
            return true;
        }

        private static PlayServStatusException InvalidResponse(
            string sourceCode,
            string message,
            int? httpStatus = null) =>
            new PlayServStatusException(new PlayServError(
                PlayServErrorCode.InvalidResponse,
                sourceCode,
                message,
                httpStatus: httpStatus));

        private static PlayServError MapHttpError(PlayServRuntimeHttpException exception) =>
            PlayServError.FromHttp(
                exception.StatusCode,
                exception.BackendCode,
                FirstNonEmpty(exception.ProblemDetail, exception.Message),
                exception.IsNetworkError,
                exception.Message.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0,
                exception.ResponseBody);

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
            return "PlayServ platform status request failed.";
        }

        [Serializable]
        private sealed class FederationWire
        {
            public string[] origins;
        }
    }
}
