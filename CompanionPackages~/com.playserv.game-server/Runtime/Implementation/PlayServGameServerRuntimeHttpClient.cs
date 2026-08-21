using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Http.Interfaces;

namespace Playserv.GameServer
{
    /// <summary>
    /// Adapts the dedicated-server credential transport to the existing Records and Code
    /// clients without ever placing <c>sk_*</c> in player SDK settings.
    /// </summary>
    internal sealed class PlayServGameServerRuntimeHttpClient : IPlayServRuntimeHttpClient
    {
        private const string ActingPlayerHeader = "X-Acting-Player";
        private readonly string _actingPlayerJwt;

        internal PlayServGameServerRuntimeHttpClient(string actingPlayerJwt = null)
        {
            _actingPlayerJwt = actingPlayerJwt;
        }

        public Task<PlayServRuntimeDataResponse> SendDataAsync(
            PlayServRuntimeDataRequest request,
            CancellationToken ct = default)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            return PlayServGameServer.SendRuntimeDataAsync(
                ShouldAttachActingPlayer(request)
                    ? WithActingPlayer(request, _actingPlayerJwt)
                    : request,
                ct);
        }

        public Task<string> GetLatestVersionAsync(string gameId, CancellationToken ct = default) =>
            throw Unsupported();

        public Task<PlayerTokenBundleDto> SignInAnonAsync(
            string clientToken,
            CancellationToken ct = default) =>
            throw Unsupported();

        public Task<PlayerRefreshResponseDto> RefreshAsync(
            string clientToken,
            string refreshToken,
            CancellationToken ct = default) =>
            throw Unsupported();

        public Task<PlayerTokenBundleDto> LoginExternalAsync(
            string clientToken,
            PlayerExternalLoginRequestDto request,
            string playerAccessToken = null,
            CancellationToken ct = default) =>
            throw Unsupported();

        public Task SignOutAsync(
            string clientToken,
            string refreshToken,
            CancellationToken ct = default) =>
            throw Unsupported();

        private static NotSupportedException Unsupported() =>
            new NotSupportedException(
                "The dedicated game-server transport supports only server-authorized runtime data and cloud-function requests.");

        private bool ShouldAttachActingPlayer(PlayServRuntimeDataRequest request)
        {
            if (string.IsNullOrEmpty(_actingPlayerJwt))
                return false;

            var method = (request.Method ?? string.Empty).Trim().ToUpperInvariant();
            if (method != "POST" && method != "PATCH" && method != "DELETE")
                return false;

            var path = (request.RelativePath ?? string.Empty).TrimStart('/');
            if (!path.StartsWith("data/tables/", StringComparison.Ordinal))
                return false;

            if (method == "POST")
            {
                var recordsSuffix = path.EndsWith("/records", StringComparison.Ordinal);
                return recordsSuffix && !path.EndsWith("/records:query", StringComparison.Ordinal);
            }

            return path.IndexOf("/records/", StringComparison.Ordinal) >= 0;
        }

        private static PlayServRuntimeDataRequest WithActingPlayer(
            PlayServRuntimeDataRequest source,
            string playerJwt)
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (source.Headers != null)
            {
                foreach (var header in source.Headers)
                {
                    if (!string.Equals(header.Key, ActingPlayerHeader, StringComparison.OrdinalIgnoreCase))
                        headers[header.Key] = header.Value;
                }
            }
            headers[ActingPlayerHeader] = playerJwt;

            return new PlayServRuntimeDataRequest
            {
                Method = source.Method,
                RelativePath = source.RelativePath,
                ClientToken = source.ClientToken,
                RequiresClientToken = source.RequiresClientToken,
                BearerToken = source.BearerToken,
                JsonBody = source.JsonBody,
                ContentType = source.ContentType,
                IdempotencyKey = source.IdempotencyKey,
                IfMatch = source.IfMatch,
                FunctionVersion = source.FunctionVersion,
                Headers = headers,
                TimeoutSeconds = source.TimeoutSeconds
            };
        }
    }
}
