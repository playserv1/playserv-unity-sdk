using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Http.Common;
using Playserv.Http.Interfaces;
using Playserv.Runtime.Abstractions;
using Playserv.Serialization;
using Playserv.Wrapper;

namespace Playserv.Matchmaking
{
    internal sealed partial class PlayServMatchmakingClient
    {
        internal const int NetworkMarginMs = 5_000;
        internal const int DefaultRequestTimeoutSeconds = 10;
        private const int MaxRoomClosedReEntries = 3;

        private readonly PlayServSettings _settings;
        private readonly IPlayServRuntimeHttpClient _http;
        private readonly IJsonCodec _json;
        private readonly Func<DateTimeOffset> _utcNow;
        private readonly Func<int, CancellationToken, Task> _delay;
        private readonly Func<double> _monotonicSeconds;

        internal PlayServMatchmakingClient(
            PlayServSettings settings,
            IPlayServRuntimeHttpClient http,
            IJsonCodec json,
            Func<DateTimeOffset> utcNow = null,
            Func<int, CancellationToken, Task> delay = null,
            Func<double> monotonicSeconds = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _json = json ?? throw new ArgumentNullException(nameof(json));
            _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
            _delay = delay ?? ((milliseconds, ct) => Task.Delay(milliseconds, ct));
            _monotonicSeconds = monotonicSeconds ?? (() => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency);
        }

        internal static PlayServMatchmakingClient CreateDefault()
        {
            var settings = PlayServ.Settings;
            var json = new NewtonsoftJsonCodec();
            var http = PlayServRuntimeHttpClientResolver.Create(
                new PlayServHttpModuleContext(settings.ToRuntimeSettings(), json));
            return new PlayServMatchmakingClient(settings, http, json);
        }

        internal static int ResolveFindMatchTimeoutSeconds(int waitMs)
        {
            if (waitMs <= 0)
                return DefaultRequestTimeoutSeconds;

            return (waitMs + NetworkMarginMs + 999) / 1000;
        }

        internal Task<PlayServMatchResult> FindMatchAsync(
            string functionSlug,
            string matchmaker,
            int waitMs,
            int searchAgeMs,
            CancellationToken ct) =>
            FindMatchAsync(new PlayServFindMatchRequest
            {
                FunctionSlug = functionSlug,
                Matchmaker = matchmaker,
                WaitMs = waitMs,
                SearchAgeMs = searchAgeMs
            }, ct);

        internal async Task<PlayServMatchResult> FindMatchAsync(
            PlayServFindMatchRequest request,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            ValidateFindArguments(request.FunctionSlug, request.WaitMs, request.SearchAgeMs);
            var parameterSnapshot = SnapshotParameters(request.Parameters, nameof(request));
            return await FindMatchCoreAsync(
                request.FunctionSlug,
                request.Matchmaker,
                parameterSnapshot,
                request.WaitMs,
                request.SearchAgeMs,
                PlayServMatchmakingOperation.FindMatch,
                ct);
        }

        private async Task<PlayServMatchResult> FindMatchCoreAsync(
            string functionSlug,
            string matchmaker,
            object parameterSnapshot,
            int waitMs,
            int searchAgeMs,
            PlayServMatchmakingOperation operation,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var bearer = await ResolvePlayerTokenAsync(ct);
            var body = _json.Serialize(new FindMatchRequestWire
            {
                matchmaker = NormalizeOptional(matchmaker),
                parameters = parameterSnapshot,
                wait_ms = waitMs,
                search_age_ms = searchAgeMs
            });
            var response = await SendMatchmakingAsync(
                new PlayServRuntimeDataRequest
                {
                    Method = "POST",
                    RelativePath = $"matchmaking/{Uri.EscapeDataString(functionSlug.Trim())}/find",
                    ClientToken = _settings.ClientToken,
                    BearerToken = bearer,
                    JsonBody = body,
                    TimeoutSeconds = ResolveFindMatchTimeoutSeconds(waitMs)
                },
                operation,
                functionSlug,
                ct);

            return ReadMatchResponse(response, operation, functionSlug, _monotonicSeconds());
        }

        internal Task<PlayServJoinGameResult> JoinGameAsync(
            string functionSlug,
            string matchmaker,
            Func<PlayServMatchReservation, CancellationToken, Task> tryEnter,
            CancellationToken ct) =>
            JoinGameAsync(new PlayServJoinGameRequest
            {
                FunctionSlug = functionSlug,
                Matchmaker = matchmaker
            }, tryEnter, ct);

        internal async Task<PlayServJoinGameResult> JoinGameAsync(
            PlayServJoinGameRequest request,
            Func<PlayServMatchReservation, CancellationToken, Task> tryEnter,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            ValidateFindArguments(request.FunctionSlug, request.WaitMs, 0);
            var parameterSnapshot = SnapshotParameters(request.Parameters, nameof(request));
            var roomClosedReEntries = 0;
            var searchStartedAt = _utcNow();
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var elapsedMs = (_utcNow() - searchStartedAt).TotalMilliseconds;
                var searchAgeMs = elapsedMs <= 0
                    ? 0
                    : elapsedMs >= int.MaxValue
                        ? int.MaxValue
                        : (int)elapsedMs;
                var result = await FindMatchCoreAsync(
                    request.FunctionSlug,
                    request.Matchmaker,
                    parameterSnapshot,
                    request.WaitMs,
                    searchAgeMs,
                    PlayServMatchmakingOperation.JoinGame,
                    ct);

                switch (result.Status)
                {
                    case PlayServMatchStatus.Matched:
                        if (tryEnter == null)
                        {
                            return new PlayServJoinGameResult(
                                PlayServJoinGameStatus.Matched,
                                result.Reservation);
                        }

                        try
                        {
                            await tryEnter(result.Reservation, ct);
                            return new PlayServJoinGameResult(
                                PlayServJoinGameStatus.Matched,
                                result.Reservation);
                        }
                        catch (PlayServRoomEntryRefusedException ex)
                            when (string.Equals(ex.ErrorCode, "room_closed", StringComparison.Ordinal))
                        {
                            roomClosedReEntries++;
                            if (roomClosedReEntries > MaxRoomClosedReEntries)
                                throw;
                            searchStartedAt = _utcNow();
                            continue;
                        }

                    case PlayServMatchStatus.Bot:
                        return new PlayServJoinGameResult(PlayServJoinGameStatus.Bot, null);

                    case PlayServMatchStatus.Searching:
                        if (result.RetryAfterMs.HasValue && result.RetryAfterMs.Value > 0)
                            await _delay(result.RetryAfterMs.Value, ct);
                        continue;

                    default:
                        return new PlayServJoinGameResult(PlayServJoinGameStatus.NotFound, null);
                }
            }
        }

        internal async Task<PlayServServerLaunchResult> LaunchServerAsync(
            string functionSlug,
            string region,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            ValidateFunctionSlug(functionSlug);
            var normalizedRegion = NormalizeOptional(region);
            if (normalizedRegion != null && normalizedRegion.Length > 64)
                throw new ArgumentOutOfRangeException(nameof(region), "region must be at most 64 characters.");

            var bearer = await ResolvePlayerTokenAsync(ct);
            var body = _json.Serialize(new LaunchServerRequestWire
            {
                region = normalizedRegion
            }, new JsonCodecOptions { IncludeNullValues = false });
            var response = await SendMatchmakingAsync(
                new PlayServRuntimeDataRequest
                {
                    Method = "POST",
                    RelativePath = $"rooms/{Uri.EscapeDataString(functionSlug.Trim())}/servers:launch",
                    ClientToken = _settings.ClientToken,
                    BearerToken = bearer,
                    JsonBody = body,
                    TimeoutSeconds = DefaultRequestTimeoutSeconds
                },
                PlayServMatchmakingOperation.LaunchServer,
                functionSlug,
                ct);

            if (response == null || string.IsNullOrWhiteSpace(response.Body))
                throw InvalidResponse(
                    PlayServMatchmakingOperation.LaunchServer,
                    functionSlug,
                    "matchmaking_empty_response",
                    "Game-server launch response body is empty.");

            LaunchServerResponseWire wire;
            try
            {
                wire = _json.Deserialize<LaunchServerResponseWire>(response.Body);
            }
            catch (Exception)
            {
                throw InvalidResponse(
                    PlayServMatchmakingOperation.LaunchServer,
                    functionSlug,
                    "matchmaking_invalid_response",
                    "Game-server launch response could not be parsed.");
            }
            if (wire == null || string.IsNullOrWhiteSpace(wire.deployment_id))
                throw InvalidResponse(
                    PlayServMatchmakingOperation.LaunchServer,
                    functionSlug,
                    "matchmaking_incomplete_response",
                    "Game-server launch response did not contain a deployment ID.");

            return new PlayServServerLaunchResult(wire.deployment_id, wire.region);
        }

        private async Task<string> ResolvePlayerTokenAsync(CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(_settings.ClientToken))
                throw new InvalidOperationException("PlayServ.Settings.ClientToken must contain a public pk_* token before using matchmaking.");

            string bearer = null;
            if (_settings.RuntimeTokenProvider != null)
                bearer = await _settings.RuntimeTokenProvider.GetTokenAsync(ct);
            else if (!string.IsNullOrWhiteSpace(_settings.PlayerAccessToken))
                bearer = _settings.PlayerAccessToken;

            if (string.IsNullOrWhiteSpace(bearer))
            {
                throw new InvalidOperationException(
                    "PlayServ matchmaking requires a player session. Connect or configure a runtime player token first.");
            }

            return bearer;
        }

        private static void ValidateFindArguments(string functionSlug, int waitMs, int searchAgeMs)
        {
            ValidateFunctionSlug(functionSlug);
            if (waitMs < 0 || waitMs > 25_000)
                throw new ArgumentOutOfRangeException(nameof(waitMs), "waitMs must be between 0 and 25000.");
            if (searchAgeMs < 0)
                throw new ArgumentOutOfRangeException(nameof(searchAgeMs), "searchAgeMs must be non-negative.");
        }

        private static void ValidateFunctionSlug(string functionSlug)
        {
            if (string.IsNullOrWhiteSpace(functionSlug))
                throw new ArgumentException("Function slug is required.", nameof(functionSlug));

            var normalized = functionSlug.Trim();
            if (normalized.IndexOf('/') >= 0 ||
                normalized.IndexOf('?') >= 0 ||
                normalized.IndexOf('#') >= 0)
            {
                throw new ArgumentException(
                    "Function slug must not contain a path, query, or fragment.",
                    nameof(functionSlug));
            }
        }

        private object SnapshotParameters(object parameters, string argumentName)
        {
            if (parameters == null)
                return null;

            try
            {
                var json = _json.Serialize(parameters);
                var snapshot = _json.ParseToPlainValue(json);
                if (!(snapshot is IDictionary))
                {
                    throw new ArgumentException(
                        "Matchmaking Parameters must serialize to a JSON object.",
                        argumentName);
                }

                return snapshot;
            }
            catch (ArgumentException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new ArgumentException(
                    "Matchmaking Parameters could not be serialized as a JSON object.",
                    argumentName,
                    exception);
            }
        }

        private PlayServMatchResult ToResult(
            FindMatchResponseWire wire,
            PlayServMatchmakingOperation operation,
            string functionSlug,
            double receivedAt)
        {
            var status = NormalizeOptional(wire.status) ?? "matched";
            var namedReservation = operation == PlayServMatchmakingOperation.JoinRoom || operation == PlayServMatchmakingOperation.HostRoom;
            if (namedReservation && status != "matched")
                throw InvalidResponse(operation, functionSlug, "matchmaking_unsupported_status",
                    "Direct room join must return a matched reservation.");
            if (wire.expires_in < 0 ||
                (namedReservation && !wire.expires_in.HasValue))
                throw InvalidResponse(operation, functionSlug, "matchmaking_incomplete_reservation",
                    "Room reservation did not contain a valid lifetime.");
            switch (status.ToLowerInvariant())
            {
                case "matched":
                    if (string.IsNullOrWhiteSpace(wire.room_name) ||
                        string.IsNullOrWhiteSpace(wire.reservation_token) ||
                        !wire.expires_at.HasValue)
                    {
                        throw InvalidResponse(
                            operation,
                            functionSlug,
                            "matchmaking_incomplete_reservation",
                            "Matched response did not contain a complete room reservation.");
                    }

                    return new PlayServMatchResult(
                        PlayServMatchStatus.Matched,
                        new PlayServMatchReservation(
                            wire.room_name,
                            wire.reservation_token,
                            wire.expires_at.Value,
                            ToConnect(wire.connect), wire.region, FreezeAttributes(wire.attributes, operation == PlayServMatchmakingOperation.HostRoom),
                            wire.expires_in, _monotonicSeconds, receivedAt),
                        wire.retry_after_ms);

                case "searching":
                    return new PlayServMatchResult(
                        PlayServMatchStatus.Searching,
                        null,
                        wire.retry_after_ms);

                case "bot":
                    return new PlayServMatchResult(PlayServMatchStatus.Bot, null, null);

                case "not_found":
                    return new PlayServMatchResult(PlayServMatchStatus.NotFound, null, null);

                default:
                    throw InvalidResponse(
                        operation,
                        functionSlug,
                        "matchmaking_unsupported_status",
                        "Matchmaking returned an unsupported status.");
            }
        }

        private async Task<PlayServRuntimeDataResponse> SendMatchmakingAsync(
            PlayServRuntimeDataRequest request,
            PlayServMatchmakingOperation operation,
            string functionSlug,
            CancellationToken ct)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                var response = await _http.SendDataAsync(request, ct);
                ct.ThrowIfCancellationRequested();
                if (response != null && (response.StatusCode < 200 || response.StatusCode > 299))
                {
                    var error = HttpFailure(request, operation, functionSlug, response.StatusCode,
                        TryReadProblemCode(response.Body), ReadProblemDetail(response.Body),
                        false, false, TryReadRetryable(response.Body));
                    error.RetryAfter = ReadRetryAfter(response.Headers);
                    throw error;
                }
                return response;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new PlayServMatchmakingException(
                    operation,
                    functionSlug,
                    new PlayServError(
                        PlayServErrorCode.Timeout,
                        "matchmaking_timeout",
                        "The PlayServ matchmaking request timed out.",
                        retryable: true),
                    null);
            }
            catch (OperationCanceledException)
            {
                throw new OperationCanceledException("The PlayServ matchmaking request was canceled.", ct);
            }
            catch (PlayServRuntimeHttpException exception)
            {
                var timeout = IsTimeout(exception);
                var retryable = TryReadRetryable(exception.ResponseBody);
                var error = HttpFailure(request, operation, functionSlug, exception.StatusCode,
                    FirstNonEmpty(exception.BackendCode, TryReadProblemCode(exception.ResponseBody), null),
                    FirstNonEmpty(exception.ProblemDetail, ReadProblemDetail(exception.ResponseBody), "PlayServ matchmaking failed."),
                    exception.IsNetworkError, timeout, retryable);
                error.RetryAfter = ReadRetryAfter(exception.ResponseHeaders);
                throw error;
            }
            catch (PlayServMatchmakingException)
            {
                throw;
            }
            catch (Exception)
            {
                throw new PlayServMatchmakingException(
                    operation,
                    functionSlug,
                    PlayServError.FromHttp(
                        0,
                        "matchmaking_network_error",
                        "The PlayServ matchmaking request failed to reach the network.",
                        true,
                        false),
                    null);
            }
        }

        private static PlayServMatchmakingException InvalidResponse(
            PlayServMatchmakingOperation operation,
            string functionSlug,
            string sourceCode,
            string message) =>
            new PlayServMatchmakingException(
                operation,
                functionSlug,
                new PlayServError(
                    PlayServErrorCode.InvalidResponse,
                    sourceCode,
                    message),
                null); // Serializer/transport exceptions can embed response bodies or admission tokens.

        private bool? TryReadRetryable(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
                return null;
            try
            {
                var value = _json.ParseToPlainValue(responseBody);
                if (_json.TryGetProperty(value, "retryable", true, out var retryable) && retryable != null)
                {
                    if (retryable is bool typed)
                        return typed;
                    if (bool.TryParse(Convert.ToString(retryable), out typed))
                        return typed;
                }
            }
            catch
            {
            }
            return null;
        }

        private string TryReadProblemCode(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
                return null;
            try
            {
                var value = _json.ParseToPlainValue(responseBody);
                if (_json.TryGetProperty(value, "code", true, out var code) && code != null)
                    return Convert.ToString(code);
                if (_json.TryGetProperty(value, "error", true, out var error) && error != null)
                    return Convert.ToString(error);
            }
            catch
            {
            }
            return null;
        }

        private static bool IsTimeout(PlayServRuntimeHttpException exception) =>
            exception != null &&
            (exception.Message.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0 ||
             exception.Message.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0 ||
             exception.BackendCode.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0);

        private static string FirstNonEmpty(string first, string second, string fallback) =>
            !string.IsNullOrWhiteSpace(first)
                ? first
                : !string.IsNullOrWhiteSpace(second) ? second : fallback;

        private static string NormalizeOptional(string value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        [Serializable]
        private sealed class FindMatchRequestWire
        {
            public string matchmaker;

            [PlayServJsonName("params")]
            public object parameters;

            public int wait_ms;
            public int search_age_ms;
        }

        [Serializable]
        private sealed class LaunchServerRequestWire
        {
            public string region;
        }

        [Serializable]
        private sealed class FindMatchResponseWire
        {
            public string status;
            public string room_name;
            public string reservation_token;
            public DateTimeOffset? expires_at;
            public int? expires_in;
            public int? retry_after_ms;
            public RoomConnectWire connect;
            public string region;
            public Dictionary<string, object> attributes;
        }

        [Serializable]
        private sealed class LaunchServerResponseWire
        {
            public string deployment_id;
            public string region;
        }
    }
}
