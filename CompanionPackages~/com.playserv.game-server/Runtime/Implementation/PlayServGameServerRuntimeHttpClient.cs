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
        private readonly Func<PlayServRuntimeDataRequest, CancellationToken, Task<PlayServRuntimeDataResponse>> _sendData;

        internal PlayServGameServerRuntimeHttpClient(string actingPlayerJwt = null,
            Func<PlayServRuntimeDataRequest, CancellationToken, Task<PlayServRuntimeDataResponse>> sendData = null)
        {
            _actingPlayerJwt = actingPlayerJwt;
            _sendData = sendData ?? PlayServGameServer.SendRuntimeDataAsync;
        }

        public Task<PlayServRuntimeDataResponse> SendDataAsync(
            PlayServRuntimeDataRequest request,
            CancellationToken ct = default)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            return _sendData(
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

            var path = (request.RelativePath ?? string.Empty).TrimStart('/').Split('?')[0];
            if (!path.StartsWith("data/tables/", StringComparison.Ordinal))
                return false;

            if (method == "POST")
            {
                return path.EndsWith("/records", StringComparison.Ordinal) ||
                    path.EndsWith("/records:bulk-create", StringComparison.Ordinal) ||
                    path.EndsWith("/records:delete-by-filter", StringComparison.Ordinal) ||
                    path.EndsWith("/records:upsert", StringComparison.Ordinal) ||
                    path.EndsWith("/records:bulk-upsert", StringComparison.Ordinal);
            }

            return path.IndexOf("/records/", StringComparison.Ordinal) >= 0 ||
                path.EndsWith("/records:by-natural-key", StringComparison.Ordinal);
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
