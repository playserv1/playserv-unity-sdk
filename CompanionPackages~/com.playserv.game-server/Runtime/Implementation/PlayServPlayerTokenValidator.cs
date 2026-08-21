using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Serialization;
using Playserv.Wrapper;

namespace Playserv.GameServer
{
    internal sealed class PlayServPlayerTokenValidator
    {
        private static readonly TimeSpan DefaultCacheTtl = TimeSpan.FromHours(1);
        private readonly object _cacheGate = new object();
        private readonly Dictionary<string, CacheEntry> _cache =
            new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
        private readonly IJsonCodec _json = new NewtonsoftJsonCodec();
        private IPlayServGameServerJwksTransport _transport =
            new PlayServUnityGameServerJwksTransport();
        private Func<DateTimeOffset> _utcNow = () => DateTimeOffset.UtcNow;

        internal async Task<PlayServPlayerTokenValidationResult> ValidateAsync(
            string backendAddress,
            TimeSpan httpTimeout,
            string jwt,
            PlayServPlayerTokenValidationOptions options,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateArguments(jwt, options);

            ParsedToken token;
            try
            {
                token = Parse(jwt.Trim());
            }
            catch
            {
                return Invalid("jwt_malformed", "The player token is malformed.");
            }

            if (!string.Equals(token.Algorithm, "RS256", StringComparison.Ordinal))
                return Invalid("jwt_algorithm_not_allowed", "Only RS256 player tokens are accepted.");
            if (string.IsNullOrWhiteSpace(token.KeyId))
                return Invalid("jwt_kid_missing", "The player token has no signing key ID.");

            SigningKey key;
            try
            {
                key = await FindKeyAsync(
                    backendAddress,
                    httpTimeout,
                    token.KeyId,
                    forceRefresh: false,
                    cancellationToken);
                if (key == null)
                {
                    key = await FindKeyAsync(
                        backendAddress,
                        httpTimeout,
                        token.KeyId,
                        forceRefresh: true,
                        cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (PlayServGameServerTransportFailure failure)
            {
                return PlayServPlayerTokenValidationResult.Invalid(
                    PlayServError.FromHttp(
                        0,
                        failure.IsTimeout ? "jwks_timeout" : "jwks_unavailable",
                        failure.IsTimeout
                            ? "The PlayServ signing keys request timed out."
                            : "The PlayServ signing keys could not be loaded.",
                        true,
                        failure.IsTimeout));
            }
            catch (JwksHttpException failure)
            {
                return PlayServPlayerTokenValidationResult.Invalid(
                    PlayServError.FromHttp(
                        failure.StatusCode,
                        "jwks_http_error",
                        "The PlayServ signing keys endpoint rejected the request.",
                        false,
                        false));
            }
            catch
            {
                return Invalid("jwks_invalid_response", "The PlayServ signing keys response was invalid.", PlayServErrorCode.InvalidResponse);
            }

            if (key == null)
                return Invalid("jwt_kid_unknown", "The player token references an unknown signing key.");

            bool signatureValid;
            try
            {
                using var rsa = RSA.Create();
                rsa.ImportParameters(new RSAParameters
                {
                    Modulus = key.Modulus,
                    Exponent = key.Exponent
                });
                signatureValid = rsa.VerifyData(
                    Encoding.ASCII.GetBytes(token.SigningInput),
                    token.Signature,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1);
            }
            catch
            {
                signatureValid = false;
            }

            if (!signatureValid)
                return Invalid("jwt_signature_invalid", "The player token signature is invalid.");

            var issuer = ReadString(token.Payload, "iss");
            if (!string.Equals(issuer, options.Issuer.Trim(), StringComparison.Ordinal))
                return Invalid("jwt_issuer_mismatch", "The player token issuer does not match.");

            var playerId = ReadString(token.Payload, "sub");
            var projectId = ReadString(token.Payload, "project_id");
            var environment = ReadString(token.Payload, "env");
            var sessionId = ReadString(token.Payload, "session_id");
            var tokenId = ReadString(token.Payload, "jti");
            if (string.IsNullOrWhiteSpace(playerId) ||
                string.IsNullOrWhiteSpace(projectId) ||
                string.IsNullOrWhiteSpace(environment) ||
                string.IsNullOrWhiteSpace(sessionId))
            {
                return Invalid("jwt_required_claim_missing", "The player token is missing a required runtime claim.");
            }

            if (!string.Equals(projectId, options.ExpectedProjectId.Trim(), StringComparison.Ordinal))
                return Invalid("jwt_project_mismatch", "The player token belongs to another project.");
            if (!string.Equals(environment, options.ExpectedEnvironment.Trim(), StringComparison.OrdinalIgnoreCase))
                return Invalid("jwt_environment_mismatch", "The player token belongs to another environment.");

            if (!TryReadUnixTime(token.Payload, "exp", out var expiresAt))
                return Invalid("jwt_expiration_missing", "The player token has no valid expiration.");
            TryReadUnixTime(token.Payload, "iat", out var issuedAt);
            TryReadUnixTime(token.Payload, "nbf", out var notBefore);
            var now = _utcNow();
            if (now > expiresAt.Value.Add(options.ClockSkew))
                return Invalid("jwt_expired", "The player token has expired.");
            if (notBefore.HasValue && now.Add(options.ClockSkew) < notBefore.Value)
                return Invalid("jwt_not_yet_valid", "The player token is not valid yet.");
            if (issuedAt.HasValue && now.Add(options.ClockSkew) < issuedAt.Value)
                return Invalid("jwt_issued_in_future", "The player token issue time is in the future.");

            return PlayServPlayerTokenValidationResult.Valid(
                new PlayServValidatedPlayerClaims(
                    playerId,
                    projectId,
                    environment,
                    sessionId,
                    ReadStringList(token.Payload, "providers"),
                    issuedAt,
                    notBefore,
                    expiresAt.Value,
                    tokenId));
        }

        internal void ConfigureForTesting(
            IPlayServGameServerJwksTransport transport,
            Func<DateTimeOffset> utcNow)
        {
            _transport = transport ?? new PlayServUnityGameServerJwksTransport();
            _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
            lock (_cacheGate)
                _cache.Clear();
        }

        private async Task<SigningKey> FindKeyAsync(
            string backendAddress,
            TimeSpan timeout,
            string keyId,
            bool forceRefresh,
            CancellationToken cancellationToken)
        {
            CacheEntry cache;
            lock (_cacheGate)
            {
                if (!_cache.TryGetValue(backendAddress, out cache))
                {
                    cache = new CacheEntry();
                    _cache.Add(backendAddress, cache);
                }
            }

            await cache.RefreshGate.WaitAsync(cancellationToken);
            try
            {
                var now = _utcNow();
                if (forceRefresh || cache.Keys == null || now >= cache.ExpiresAt)
                {
                    var response = await _transport.GetAsync(
                        backendAddress.TrimEnd('/') + "/.well-known/jwks.json",
                        timeout,
                        cancellationToken);
                    if (response == null || response.StatusCode < 200 || response.StatusCode > 299)
                        throw new JwksHttpException(response?.StatusCode ?? 0);
                    cache.Keys = ParseKeys(response.Body);
                    cache.ExpiresAt = now.Add(ParseMaxAge(response.CacheControl) ?? DefaultCacheTtl);
                }

                foreach (var key in cache.Keys)
                {
                    if (string.Equals(key.KeyId, keyId, StringComparison.Ordinal))
                        return key;
                }
                return null;
            }
            finally
            {
                cache.RefreshGate.Release();
            }
        }

        private IReadOnlyList<SigningKey> ParseKeys(string json)
        {
            var wire = _json.Deserialize<JwksWire>(json);
            if (wire?.keys == null)
                throw new InvalidOperationException("JWKS keys missing.");
            var result = new List<SigningKey>();
            foreach (var key in wire.keys)
            {
                if (key == null ||
                    !string.Equals(key.kty, "RSA", StringComparison.Ordinal) ||
                    !string.Equals(key.alg, "RS256", StringComparison.Ordinal) ||
                    !string.Equals(key.use, "sig", StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(key.kid))
                    continue;
                result.Add(new SigningKey(
                    key.kid,
                    DecodeBase64Url(key.n),
                    DecodeBase64Url(key.e)));
            }
            return result;
        }

        private ParsedToken Parse(string jwt)
        {
            var segments = jwt.Split('.');
            if (segments.Length != 3 || segments[0].Length == 0 || segments[1].Length == 0 || segments[2].Length == 0)
                throw new FormatException();
            var header = _json.ParseToPlainValue(Encoding.UTF8.GetString(DecodeBase64Url(segments[0])));
            var payload = _json.ParseToPlainValue(Encoding.UTF8.GetString(DecodeBase64Url(segments[1])));
            if (!(header is IDictionary<string, object> headerMap) ||
                !(payload is IDictionary<string, object> payloadMap))
                throw new FormatException();
            return new ParsedToken(
                ReadString(headerMap, "alg"),
                ReadString(headerMap, "kid"),
                payloadMap,
                segments[0] + "." + segments[1],
                DecodeBase64Url(segments[2]));
        }

        private static byte[] DecodeBase64Url(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new FormatException();
            var padded = value.Replace('-', '+').Replace('_', '/');
            switch (padded.Length % 4)
            {
                case 2: padded += "=="; break;
                case 3: padded += "="; break;
                case 1: throw new FormatException();
            }
            return Convert.FromBase64String(padded);
        }

        private static string ReadString(IDictionary<string, object> values, string name)
        {
            if (values == null || !values.TryGetValue(name, out var value) || value == null)
                return null;
            return value as string ?? Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static IReadOnlyList<string> ReadStringList(
            IDictionary<string, object> values,
            string name)
        {
            if (!values.TryGetValue(name, out var value) || value == null)
                return Array.Empty<string>();
            if (value is string single)
                return new[] { single };
            if (!(value is IEnumerable enumerable))
                return Array.Empty<string>();
            var result = new List<string>();
            foreach (var item in enumerable)
            {
                var text = item as string ?? Convert.ToString(item, CultureInfo.InvariantCulture);
                if (!string.IsNullOrWhiteSpace(text))
                    result.Add(text);
            }
            return result;
        }

        private static bool TryReadUnixTime(
            IDictionary<string, object> values,
            string name,
            out DateTimeOffset? result)
        {
            result = null;
            if (!values.TryGetValue(name, out var value) || value == null)
                return false;
            if (!long.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
                return false;
            try
            {
                result = DateTimeOffset.FromUnixTimeSeconds(seconds);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static TimeSpan? ParseMaxAge(string cacheControl)
        {
            if (string.IsNullOrWhiteSpace(cacheControl))
                return null;
            foreach (var part in cacheControl.Split(','))
            {
                var trimmed = part.Trim();
                if (!trimmed.StartsWith("max-age=", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (long.TryParse(trimmed.Substring(8).Trim().Trim('"'), out var seconds) && seconds >= 0)
                    return TimeSpan.FromSeconds(Math.Min(seconds, 86400));
            }
            return null;
        }

        private static void ValidateArguments(
            string jwt,
            PlayServPlayerTokenValidationOptions options)
        {
            if (string.IsNullOrWhiteSpace(jwt))
                throw new ArgumentException("A player JWT is required.", nameof(jwt));
            if (jwt.IndexOf('\r') >= 0 || jwt.IndexOf('\n') >= 0)
                throw new ArgumentException("The player JWT cannot contain line breaks.", nameof(jwt));
            if (options == null)
                throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrWhiteSpace(options.ExpectedProjectId))
                throw new ArgumentException("ExpectedProjectId is required.", nameof(options));
            if (string.IsNullOrWhiteSpace(options.ExpectedEnvironment))
                throw new ArgumentException("ExpectedEnvironment is required.", nameof(options));
            if (string.IsNullOrWhiteSpace(options.Issuer))
                throw new ArgumentException("Issuer is required.", nameof(options));
            if (options.ClockSkew < TimeSpan.Zero || options.ClockSkew > TimeSpan.FromMinutes(10))
                throw new ArgumentOutOfRangeException(nameof(options), "ClockSkew must be between zero and ten minutes.");
        }

        private static PlayServPlayerTokenValidationResult Invalid(
            string sourceCode,
            string message,
            PlayServErrorCode code = PlayServErrorCode.Unauthorized) =>
            PlayServPlayerTokenValidationResult.Invalid(
                new PlayServError(code, sourceCode, message, retryable: false));

        private sealed class CacheEntry
        {
            internal readonly SemaphoreSlim RefreshGate = new SemaphoreSlim(1, 1);
            internal IReadOnlyList<SigningKey> Keys;
            internal DateTimeOffset ExpiresAt;
        }

        private sealed class SigningKey
        {
            internal SigningKey(string keyId, byte[] modulus, byte[] exponent)
            {
                KeyId = keyId;
                Modulus = modulus;
                Exponent = exponent;
            }
            internal string KeyId { get; }
            internal byte[] Modulus { get; }
            internal byte[] Exponent { get; }
        }

        private sealed class ParsedToken
        {
            internal ParsedToken(
                string algorithm,
                string keyId,
                IDictionary<string, object> payload,
                string signingInput,
                byte[] signature)
            {
                Algorithm = algorithm;
                KeyId = keyId;
                Payload = payload;
                SigningInput = signingInput;
                Signature = signature;
            }
            internal string Algorithm { get; }
            internal string KeyId { get; }
            internal IDictionary<string, object> Payload { get; }
            internal string SigningInput { get; }
            internal byte[] Signature { get; }
        }

        [Serializable]
        private sealed class JwksWire { public JwkWire[] keys; }
        [Serializable]
        private sealed class JwkWire
        {
            public string kty;
            public string use;
            public string alg;
            public string kid;
            public string n;
            public string e;
        }

        private sealed class JwksHttpException : Exception
        {
            internal JwksHttpException(int statusCode) { StatusCode = statusCode; }
            internal int StatusCode { get; }
        }
    }
}
