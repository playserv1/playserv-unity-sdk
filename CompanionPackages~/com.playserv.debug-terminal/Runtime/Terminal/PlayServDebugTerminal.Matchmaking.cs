using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Matchmaking;
using Playserv.Wrapper;

namespace Playserv.DebugTerminal
{
    public sealed partial class PlayServDebugTerminal
    {
        private CancellationTokenSource _matchCancellation;
        private DateTimeOffset? _activeMatchStartedAt;
        private string _activeMatchSummary = "none";
        private string _lastMatchSummary = "none";

        private async Task ExecuteMatchCommandAsync(IReadOnlyList<string> parts)
        {
            var operation = parts.Count > 1 ? parts[1].ToLowerInvariant() : "status";
            switch (operation)
            {
                case "find":
                    await ExecuteFindMatchAsync(parts);
                    return;
                case "join":
                    await ExecuteJoinGameAsync(parts);
                    return;
                case "launch":
                    await ExecuteLaunchServerAsync(parts);
                    return;
                case "status":
                    PrintMatchStatus();
                    return;
                case "cancel":
                    CancelActiveMatch(silent: false);
                    return;
                default:
                    AddLog("Usage: match <find|join|launch|status|cancel> ...");
                    return;
            }
        }

        private async Task ExecuteFindMatchAsync(IReadOnlyList<string> parts)
        {
            if (!TryBeginMatchCommand(parts, "find", out var arguments, out var slug, out var cancellation))
                return;
            try
            {
                if (!TryReadMatchOptions(arguments, 0, includeSearchAge: true, out var waitMs, out var searchAgeMs, out var parameters))
                    return;
                _activeMatchSummary = $"find {slug}";
                var result = await PlayServMatchmaking.FindMatchAsync(
                    new PlayServFindMatchRequest
                    {
                        FunctionSlug = slug,
                        Matchmaker = arguments.Get("matchmaker"),
                        Parameters = parameters,
                        WaitMs = waitMs,
                        SearchAgeMs = searchAgeMs
                    },
                    cancellation.Token);
                _lastMatchSummary =
                    $"find; status={result.Status}; {FormatMatchReservation(result.Reservation)}; " +
                    $"retryAfterMs={result.RetryAfterMs?.ToString() ?? "-"}";
                AddLog(_lastMatchSummary);
            }
            catch (OperationCanceledException)
            {
                RecordMatchCancellation("find");
            }
            catch (PlayServMatchmakingException exception)
            {
                RecordMatchFailure(exception);
            }
            finally
            {
                EndMatchCommand(cancellation);
            }
        }

        private async Task ExecuteJoinGameAsync(IReadOnlyList<string> parts)
        {
            if (!TryBeginMatchCommand(parts, "join", out var arguments, out var slug, out var cancellation))
                return;
            try
            {
                if (!TryReadMatchOptions(
                        arguments,
                        PlayServJoinGameRequest.DefaultWaitMs,
                        includeSearchAge: false,
                        out var waitMs,
                        out _,
                        out var parameters))
                    return;
                _activeMatchSummary = $"join {slug}";
                var result = await PlayServMatchmaking.JoinGameAsync(
                    new PlayServJoinGameRequest
                    {
                        FunctionSlug = slug,
                        Matchmaker = arguments.Get("matchmaker"),
                        Parameters = parameters,
                        WaitMs = waitMs
                    },
                    tryEnter: null,
                    cancellationToken: cancellation.Token);
                _lastMatchSummary = $"join; status={result.Status}; {FormatMatchReservation(result.Reservation)}";
                AddLog(_lastMatchSummary);
            }
            catch (OperationCanceledException)
            {
                RecordMatchCancellation("join");
            }
            catch (PlayServMatchmakingException exception)
            {
                RecordMatchFailure(exception);
            }
            finally
            {
                EndMatchCommand(cancellation);
            }
        }

        private async Task ExecuteLaunchServerAsync(IReadOnlyList<string> parts)
        {
            if (!TryBeginMatchCommand(parts, "launch", out var arguments, out var slug, out var cancellation))
                return;
            try
            {
                var region = arguments.Get("region");
                if (region != null && region.Length > 64)
                {
                    AddLog("Matchmaking region must not exceed 64 characters.");
                    return;
                }
                _activeMatchSummary = $"launch {slug}";
                var result = await PlayServMatchmaking.LaunchServerAsync(slug, region, cancellation.Token);
                _lastMatchSummary =
                    $"launch accepted; deployment={result.DeploymentId}; region={result.Region ?? "-"}; " +
                    "room appears after server self-registration";
                AddLog(_lastMatchSummary);
            }
            catch (OperationCanceledException)
            {
                RecordMatchCancellation("launch");
            }
            catch (PlayServMatchmakingException exception)
            {
                RecordMatchFailure(exception);
            }
            finally
            {
                EndMatchCommand(cancellation);
            }
        }

        private bool TryBeginMatchCommand(
            IReadOnlyList<string> parts,
            string operation,
            out DebugTerminalArguments arguments,
            out string slug,
            out CancellationTokenSource cancellation)
        {
            arguments = null;
            slug = null;
            cancellation = null;
            if (_matchCancellation != null)
            {
                AddLog($"Matchmaking operation is already running: {_activeMatchSummary}.");
                return false;
            }
            if (parts.Count < 3 || string.IsNullOrWhiteSpace(parts[2]))
            {
                AddLog($"Usage: match {operation} <functionSlug> [options]");
                return false;
            }

            var valueOptions = operation == "launch"
                ? new[] { "region" }
                : new[]
                {
                    "matchmaker", "wait", "wait-ms", "wait_ms", "age", "search-age-ms",
                    "search_age_ms", "params"
                };
            if (!DebugTerminalArguments.TryParse(
                    parts,
                    3,
                    valueOptions,
                    Array.Empty<string>(),
                    out arguments,
                    out var error))
            {
                AddLog(error);
                return false;
            }
            if (arguments.Positionals.Count != 0)
            {
                AddLog($"Unexpected matchmaking argument '{arguments.Positionals[0]}'.");
                return false;
            }

            slug = parts[2];
            cancellation = new CancellationTokenSource();
            _matchCancellation = cancellation;
            _activeMatchStartedAt = DateTimeOffset.UtcNow;
            _activeMatchSummary = $"{operation} {slug}";
            AddLog($"Matchmaking => {_activeMatchSummary}");
            return true;
        }

        private bool TryReadMatchOptions(
            DebugTerminalArguments arguments,
            int defaultWaitMs,
            bool includeSearchAge,
            out int waitMs,
            out int searchAgeMs,
            out object parameters)
        {
            waitMs = defaultWaitMs;
            searchAgeMs = 0;
            parameters = null;
            var waitText = GetFirst(arguments, "wait-ms", "wait_ms", "wait");
            if (waitText != null && (!int.TryParse(waitText, out waitMs) || waitMs < 0 || waitMs > 25000))
            {
                AddLog("Matchmaking wait_ms must be between 0 and 25000.");
                return false;
            }
            var ageText = GetFirst(arguments, "search-age-ms", "search_age_ms", "age");
            if (!includeSearchAge && ageText != null)
            {
                AddLog("search_age_ms is computed by JoinGameAsync and cannot be supplied for match join.");
                return false;
            }
            if (ageText != null && (!int.TryParse(ageText, out searchAgeMs) || searchAgeMs < 0))
            {
                AddLog("Matchmaking search_age_ms must be zero or greater.");
                return false;
            }
            if (!DebugTerminalArguments.TryParseJson(
                    arguments.Get("params"),
                    requireObject: true,
                    out parameters,
                    out var error))
            {
                AddLog(error);
                return false;
            }
            return true;
        }

        private static string GetFirst(DebugTerminalArguments arguments, params string[] names)
        {
            foreach (var name in names)
            {
                var value = arguments.Get(name);
                if (value != null)
                    return value;
            }
            return null;
        }

        internal static string FormatMatchReservation(PlayServMatchReservation reservation)
        {
            return reservation == null
                ? "room=-; expires=-"
                : $"room={reservation.RoomName}; expires={reservation.ExpiresAt:O}";
        }

        private void PrintMatchStatus()
        {
            if (_matchCancellation == null)
            {
                AddLog($"Matchmaking: idle; last={_lastMatchSummary}");
                return;
            }
            var elapsed = _activeMatchStartedAt.HasValue
                ? (DateTimeOffset.UtcNow - _activeMatchStartedAt.Value).TotalMilliseconds
                : 0d;
            AddLog($"Matchmaking: running; operation={_activeMatchSummary}; elapsed={elapsed:F0}ms");
        }

        private void CancelActiveMatch(bool silent)
        {
            if (_matchCancellation == null)
            {
                if (!silent)
                    AddLog("No matchmaking operation is running.");
                return;
            }
            _matchCancellation.Cancel();
            if (!silent)
                AddLog($"Cancellation requested for matchmaking '{_activeMatchSummary}'.");
        }

        private void RecordMatchFailure(PlayServMatchmakingException exception)
        {
            _lastMatchSummary =
                $"operation={exception.Operation}; slug={exception.FunctionSlug}; error={FormatError(exception.UnifiedError)}";
            _status = $"Matchmaking failed: {exception.UnifiedError.Code}";
            AddLog(_lastMatchSummary);
        }

        private void RecordMatchCancellation(string operation)
        {
            _lastMatchSummary = $"{operation}; canceled";
            _status = "Matchmaking canceled";
            AddLog(_lastMatchSummary);
        }

        private void EndMatchCommand(CancellationTokenSource cancellation)
        {
            if (ReferenceEquals(_matchCancellation, cancellation))
            {
                _matchCancellation = null;
                _activeMatchStartedAt = null;
                _activeMatchSummary = "none";
            }
            cancellation?.Dispose();
        }
    }
}
