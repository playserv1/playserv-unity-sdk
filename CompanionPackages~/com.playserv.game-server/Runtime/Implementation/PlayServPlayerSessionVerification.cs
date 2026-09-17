using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Proxy.Implementation;
using Playserv.Serialization;
using Playserv.Wrapper;

namespace Playserv.GameServer
{
    /// <summary>Authoritative online session verdict. Unknown future reasons remain available unchanged.</summary>
    public sealed class PlayServPlayerSessionVerificationResult
    {
        internal PlayServPlayerSessionVerificationResult(bool valid, string reason) { Valid = valid; Reason = reason; }
        public bool Valid { get; }
        public string Reason { get; }
    }

    public static partial class PlayServGameServer
    {
        /// <summary>
        /// Checks signature, scope, revocation and player status through POST /players/sessions:verify.
        /// A negative verdict is a successful response, not an exception. Does not replace reservation consume.
        /// The token is used only for this call; never persist it or log it in game code.
        /// </summary>
        public static async Task<PlayServPlayerSessionVerificationResult> VerifyPlayerSessionAsync(
            string playerId, string token, CancellationToken ct = default)
        {
            EnsureSupportedBuild();
            ct.ThrowIfCancellationRequested();
            GameServerServiceValues.RequireText(playerId, nameof(playerId));
            GameServerServiceValues.RequireText(token, nameof(token));
            var context = GetContext();
            try
            {
                var response = await SendAsync(context, "POST", "players/sessions:verify",
                    PlayServGameServerJson.Serialize(new { player_id = playerId, token }), context.HttpTimeout,
                    "player session verification", false, ct);
                var body = GameServerServiceValues.Object(response.Body);
                if (body == null || !body.TryGetValue("valid", out var valid) || !(valid is bool) ||
                    (body.TryGetValue("reason", out var reason) && reason != null && !(reason is string)))
                    throw InvalidResponse("player session verification");
                return new PlayServPlayerSessionVerificationResult((bool)valid,
                    GameServerServiceValues.SafeText(body.TryGetValue("reason", out var value) ? value as string : null, token));
            }
            catch (OperationCanceledException) { throw; }
            catch (PlayServGameServerException ex)
            {
                var e = ex.UnifiedError;
                throw new PlayServGameServerException(new PlayServError(e.Code,
                    GameServerServiceValues.SafeText(e.SourceCode, token), GameServerServiceValues.SafeText(e.Message, token),
                    e.HttpStatus, e.TransportCode, e.Retryable));
            }
            catch
            {
                // Neither serializer nor custom transport exceptions may expose the submitted credential.
                throw InvalidResponse("player session verification");
            }
        }
    }

    internal static class GameServerServiceValues
    {
        internal static readonly NewtonsoftJsonCodec Codec = new NewtonsoftJsonCodec();
        internal static readonly TransportLogRedactor Redactor = new TransportLogRedactor(Codec);

        internal static void RequireText(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A non-empty value is required.", name);
            foreach (var c in value)
                if (char.IsControl(c)) throw new ArgumentException("Control characters are not allowed.", name);
        }

        internal static IDictionary<string, object> Object(string json)
        {
            try { return Codec.ParseToPlainValue(json) as IDictionary<string, object>; }
            catch { return null; }
        }

        internal static string SafeText(string text, string secret = null) => text == null ? null :
            Redactor.RedactText(string.IsNullOrEmpty(secret) ? text : text.Replace(secret, "[REDACTED]"));
    }
}
