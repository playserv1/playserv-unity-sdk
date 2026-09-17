using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Http.Interfaces;
using Playserv.Serialization;
using Playserv.Wrapper;

namespace Playserv.Matchmaking
{
    internal sealed partial class PlayServMatchmakingClient
    {
        internal Task<PlayServMatchResult> HostRoomAsync(
            PlayServHostRoomRequest request, PlayServRoomHostOptions options, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (request == null) throw new ArgumentNullException(nameof(request));
            var startedAt = _monotonicSeconds();
            var slug = request.FunctionSlug;
            var region = request.Region;
            var timeout = options?.Timeout ?? TimeSpan.FromSeconds(45);
            ValidateRoomType(slug);
            if (region != null && region.Length > 32)
                throw new ArgumentException("Region must be at most 32 characters.", nameof(request));
            if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(options), "Host timeout must be positive and at most Int32.MaxValue milliseconds.");

            // Serialize the exact request once, before any await, without retaining caller-owned objects.
            string body;
            try
            {
                var fields = new Dictionary<string, object>();
                if (request.Attributes != null)
                {
                    var snapshot = _json.ToPlainValue(_json.Deserialize<object>(_json.Serialize(request.Attributes),
                        new JsonCodecOptions { ParseDates = false, IgnoreMetadataProperties = true }));
                    if (!(snapshot is IDictionary attributes)) throw new FormatException();
                    foreach (DictionaryEntry entry in attributes)
                        if (!(entry.Key is string) || entry.Value is IDictionary || entry.Value is IList)
                            throw new FormatException();
                    if (Encoding.UTF8.GetByteCount(_json.Serialize(snapshot)) > 2048) throw new FormatException();
                    fields.Add("attributes", snapshot);
                }
                if (region != null) fields.Add("region", region);
                body = _json.Serialize(fields);
            }
            catch (Exception)
            {
                // Custom DTO getters and serializers can embed requested content in their exception text.
                throw new ArgumentException("Host attributes must serialize to a flat JSON object of at most 2048 UTF-8 bytes.", nameof(request));
            }
            return HostRoomCoreAsync(slug, body, timeout, startedAt, ct);
        }

        private async Task<PlayServMatchResult> HostRoomCoreAsync(
            string slug, string body, TimeSpan timeout, double startedAt, CancellationToken ct)
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var initialRemaining = HostRemainingSeconds(slug, timeout, startedAt);
            budget.CancelAfter(TimeSpan.FromSeconds(initialRemaining));
            try
            {
                var bearer = await AwaitJoinBudget(ResolveHostPlayerTokenAsync(slug, budget.Token), budget.Token);
                ct.ThrowIfCancellationRequested();
                budget.Token.ThrowIfCancellationRequested();
                var remaining = HostRemainingSeconds(slug, timeout, startedAt);
                var response = await AwaitJoinBudget(SendMatchmakingAsync(new PlayServRuntimeDataRequest
                {
                    Method = "POST",
                    RelativePath = "rooms/" + Uri.EscapeDataString(slug) + ":host",
                    ClientToken = _settings.ClientToken,
                    BearerToken = bearer,
                    JsonBody = body,
                    TimeoutSeconds = Math.Max(1, (int)Math.Ceiling(remaining))
                }, PlayServMatchmakingOperation.HostRoom, slug, budget.Token), budget.Token);
                ct.ThrowIfCancellationRequested();
                budget.Token.ThrowIfCancellationRequested();
                HostRemainingSeconds(slug, timeout, startedAt);
                return ReadMatchResponse(response, PlayServMatchmakingOperation.HostRoom, slug, _monotonicSeconds());
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException("The PlayServ host request was canceled.", ct);
            }
            catch (OperationCanceledException) when (budget.IsCancellationRequested)
            {
                throw HostBudgetExpired(slug);
            }
        }

        private double HostRemainingSeconds(string slug, TimeSpan timeout, double startedAt)
        {
            var remaining = timeout.TotalSeconds - (_monotonicSeconds() - startedAt);
            if (remaining <= 0) throw HostBudgetExpired(slug);
            return remaining;
        }

        private async Task<string> ResolveHostPlayerTokenAsync(string slug, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(_settings.ClientToken))
                throw new InvalidOperationException("PlayServ.Settings.ClientToken must contain a public pk_* token before using matchmaking.");
            var bearer = _settings.PlayerAccessToken;
            if (_settings.RuntimeTokenProvider != null)
            {
                const string source = "room_host_authentication_failed";
                const string message = "The player token provider failed while preparing the room host request.";
                try { bearer = await _settings.RuntimeTokenProvider.GetTokenAsync(ct); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (OperationCanceledException)
                {
                    throw new PlayServMatchmakingException(PlayServMatchmakingOperation.HostRoom, slug,
                        new PlayServError(PlayServErrorCode.Timeout, source, message));
                }
                catch (PlayServRuntimeHttpException error)
                {
                    throw new PlayServMatchmakingException(PlayServMatchmakingOperation.HostRoom, slug,
                        PlayServError.FromHttp(error.StatusCode, source, message, error.IsNetworkError, IsTimeout(error)));
                }
                catch (Exception)
                {
                    throw new PlayServMatchmakingException(PlayServMatchmakingOperation.HostRoom, slug,
                        new PlayServError(PlayServErrorCode.Unknown, source, message));
                }
            }
            if (string.IsNullOrWhiteSpace(bearer))
                throw new InvalidOperationException("PlayServ matchmaking requires a player session. Connect or configure a runtime player token first.");
            return bearer;
        }

        private static PlayServMatchmakingException HostBudgetExpired(string slug) =>
            new PlayServMatchmakingException(PlayServMatchmakingOperation.HostRoom, slug,
                new PlayServError(PlayServErrorCode.Timeout, "room_host_timeout",
                    "The room host budget expired. A room may already have been created."));

        private void ValidateHostReservation(object plain)
        {
            if (!_json.TryGetProperty(plain, "status", false, out var status) || !(status is string text) || text != "matched" ||
                !_json.TryGetProperty(plain, "room_name", false, out var name) || !(name is string roomName) || !RoomNamePattern.IsMatch(roomName) ||
                !_json.TryGetProperty(plain, "reservation_token", false, out var token) || !(token is string secret) || string.IsNullOrWhiteSpace(secret) ||
                !_json.TryGetProperty(plain, "expires_at", false, out var expiry) || !(expiry is string) ||
                !_json.TryGetProperty(plain, "expires_in", false, out var ttl) || ttl == null)
                throw new FormatException();
        }
    }
}
