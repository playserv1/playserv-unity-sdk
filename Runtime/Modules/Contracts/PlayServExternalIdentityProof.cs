using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

namespace Playserv.Identity
{
    public static class PlayServIdentityProviderIds
    {
        public const string Apple = "apple";
        public const string Google = "google";
        public const string Facebook = "facebook";
        public const string Epic = "epic";
        public const string Steam = "steam";
        public const string PlayServToken = "playserv-token";
        public const string PlayServWebhook = "playserv-webhook";
    }

    public static class PlayServIdentityProviderModes
    {
        public const string EpicLauncherExchangeCode = "launcher_exchange_code";
    }

    /// <summary>
    /// Supplies custom device signals for anonymous and provider login. Implementations must
    /// obtain any required player consent before returning signals. An explicit provider
    /// overrides the built-in Android/iOS collector, including when it returns null.
    /// </summary>
    public interface IPlayServPlayerFingerprintProvider
    {
        Task<PlayServPlayerFingerprint> GetFingerprintAsync(
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Validated device-signal envelope. Signals are used for abuse prevention and
    /// observability only; they are not an account recovery credential.
    /// </summary>
    public sealed class PlayServPlayerFingerprint
    {
        private static readonly HashSet<string> Platforms = new HashSet<string>(StringComparer.Ordinal)
        {
            "ios", "android", "web", "windows", "macos", "linux", "console"
        };

        private static readonly HashSet<string> StableKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "platform", "device_id_hash", "device_model", "ua_stable", "hardware_class"
        };

        private static readonly HashSet<string> SoftKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "os_version", "app_version", "sdk_version", "locale", "timezone", "screen", "install_uuid"
        };

        private static readonly HashSet<string> HardwareClasses = new HashSet<string>(StringComparer.Ordinal)
        {
            "high", "mid", "low"
        };

        public PlayServPlayerFingerprint(
            IReadOnlyDictionary<string, object> stable,
            IReadOnlyDictionary<string, object> soft = null)
        {
            Stable = CopyAndValidate(stable, StableKeys, stableNamespace: true);
            Soft = CopyAndValidate(soft, SoftKeys, stableNamespace: false);

            if (!Stable.TryGetValue("platform", out var platformValue) ||
                !(platformValue is string platform) ||
                !Platforms.Contains(platform))
            {
                throw new ArgumentException(
                    "Fingerprint stable.platform must be one of: ios, android, web, windows, macos, linux, console.",
                    nameof(stable));
            }

            var isMobile = platform == "ios" || platform == "android";
            if (!isMobile && Stable.ContainsKey("device_model"))
                throw new ArgumentException("Fingerprint device_model is allowed only on iOS and Android.", nameof(stable));
            if (platform == "web" && Stable.ContainsKey("device_id_hash"))
                throw new ArgumentException("Fingerprint device_id_hash is not allowed on WebGL.", nameof(stable));
            if (platform != "web" &&
                (Stable.ContainsKey("ua_stable") || Stable.ContainsKey("hardware_class")))
            {
                throw new ArgumentException(
                    "Fingerprint ua_stable and hardware_class are allowed only on web.",
                    nameof(stable));
            }
        }

        public IReadOnlyDictionary<string, object> Stable { get; }

        public IReadOnlyDictionary<string, object> Soft { get; }

        private static IReadOnlyDictionary<string, object> CopyAndValidate(
            IReadOnlyDictionary<string, object> source,
            HashSet<string> allowedKeys,
            bool stableNamespace)
        {
            var copy = new Dictionary<string, object>(StringComparer.Ordinal);
            if (source == null)
                return new ReadOnlyDictionary<string, object>(copy);

            foreach (var pair in source)
            {
                var key = pair.Key?.Trim();
                if (string.IsNullOrWhiteSpace(key) || !allowedKeys.Contains(key))
                    throw new ArgumentException($"Fingerprint field '{pair.Key}' is not allowed.", nameof(source));

                if (key == "screen")
                {
                    copy[key] = ValidateScreen(pair.Value);
                    continue;
                }

                if (!(pair.Value is string text) || string.IsNullOrWhiteSpace(text) || text.Length > 256)
                    throw new ArgumentException($"Fingerprint field '{key}' must be a non-empty string up to 256 characters.", nameof(source));

                var normalized = text.Trim();
                if (key == "hardware_class" && !HardwareClasses.Contains(normalized))
                    throw new ArgumentException("Fingerprint hardware_class must be high, mid, or low.", nameof(source));

                if (key == "device_id_hash" && !IsLowercaseSha256(normalized))
                {
                    throw new ArgumentException(
                        "Fingerprint device_id_hash must be exactly 64 lowercase hexadecimal characters.",
                        nameof(source));
                }

                if (stableNamespace && key == "device_model" && normalized.Length > 128)
                    throw new ArgumentException("Fingerprint device_model cannot exceed 128 characters.", nameof(source));

                copy[key] = normalized;
            }

            return new ReadOnlyDictionary<string, object>(copy);
        }

        private static bool IsLowercaseSha256(string value)
        {
            if (value == null || value.Length != 64)
                return false;

            foreach (var character in value)
            {
                if ((character < '0' || character > '9') &&
                    (character < 'a' || character > 'f'))
                {
                    return false;
                }
            }
            return true;
        }

        private static IReadOnlyDictionary<string, object> ValidateScreen(object value)
        {
            if (!(value is IReadOnlyDictionary<string, object> screen))
                throw new ArgumentException("Fingerprint soft.screen must contain integer width and height values.");

            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var pair in screen)
            {
                if (pair.Key != "width" && pair.Key != "height")
                    throw new ArgumentException($"Fingerprint screen field '{pair.Key}' is not allowed.");
                if (!(pair.Value is int dimension) || dimension <= 0)
                    throw new ArgumentException($"Fingerprint screen field '{pair.Key}' must be a positive integer.");
                result[pair.Key] = dimension;
            }

            if (!result.ContainsKey("width") || !result.ContainsKey("height"))
                throw new ArgumentException("Fingerprint soft.screen requires width and height.");
            return new ReadOnlyDictionary<string, object>(result);
        }
    }

    /// <summary>
    /// Unverified provider credential accepted by PlayServ player authentication.
    /// It must never be treated as a PlayServ session until the backend validates it.
    /// </summary>
    public sealed class PlayServExternalIdentityProof
    {
        public PlayServExternalIdentityProof(
            string providerId,
            string idToken,
            string authorizationCode)
        {
            if (string.IsNullOrWhiteSpace(providerId))
                throw new ArgumentException("Identity provider id is required.", nameof(providerId));

            ProviderId = providerId.Trim();
            IdToken = idToken ?? string.Empty;
            AuthorizationCode = authorizationCode ?? string.Empty;

            if (!HasIdToken && !HasAuthorizationCode)
            {
                throw new ArgumentException(
                    "An identity token or authorization code is required.",
                    nameof(idToken));
            }

            ProviderToken = HasIdToken ? IdToken : AuthorizationCode;
            Mode = string.Empty;
            Nonce = string.Empty;
        }

        private PlayServExternalIdentityProof(
            string providerId,
            string providerToken,
            string mode,
            string nonce)
        {
            if (string.IsNullOrWhiteSpace(providerId))
                throw new ArgumentException("Identity provider id is required.", nameof(providerId));

            if (string.IsNullOrWhiteSpace(providerToken))
                throw new ArgumentException("Provider token is required.", nameof(providerToken));

            ProviderId = providerId.Trim();
            ProviderToken = providerToken.Trim();
            Mode = mode?.Trim() ?? string.Empty;
            Nonce = nonce?.Trim() ?? string.Empty;
            IdToken = string.Empty;
            AuthorizationCode = string.Empty;
        }

        public static PlayServExternalIdentityProof FromProviderToken(
            string providerId,
            string providerToken,
            string mode = null,
            string nonce = null) =>
            new PlayServExternalIdentityProof(providerId, providerToken, mode, nonce);

        public static PlayServExternalIdentityProof FromAppleIdToken(string idToken, string nonce = null) =>
            FromProviderToken(PlayServIdentityProviderIds.Apple, idToken, nonce: nonce);

        public static PlayServExternalIdentityProof FromGoogleIdToken(string idToken, string nonce = null) =>
            FromProviderToken(PlayServIdentityProviderIds.Google, idToken, nonce: nonce);

        public static PlayServExternalIdentityProof FromFacebookLimitedLoginToken(
            string authenticationToken,
            string nonce)
        {
            if (string.IsNullOrWhiteSpace(nonce))
                throw new ArgumentException("Facebook Limited Login requires a nonce.", nameof(nonce));
            return FromProviderToken(
                PlayServIdentityProviderIds.Facebook,
                authenticationToken,
                nonce: nonce);
        }

        public static PlayServExternalIdentityProof FromEpicExternalAuthToken(
            string externalAuthToken,
            bool launcherExchangeCode = false) =>
            FromProviderToken(
                PlayServIdentityProviderIds.Epic,
                externalAuthToken,
                launcherExchangeCode ? PlayServIdentityProviderModes.EpicLauncherExchangeCode : null);

        public static PlayServExternalIdentityProof FromSteamTicket(string ticket) =>
            FromProviderToken(PlayServIdentityProviderIds.Steam, ticket);

        public static PlayServExternalIdentityProof FromPlayServToken(string token) =>
            FromProviderToken(PlayServIdentityProviderIds.PlayServToken, token);

        public string ProviderId { get; }

        public string IdToken { get; }

        public string AuthorizationCode { get; }

        public string ProviderToken { get; }

        public string Mode { get; }

        public string Nonce { get; }

        /// <summary>
        /// Optional, game-selected cosmetic name for login or first provider link. Not a rename
        /// operation, not trusted identity, and never included in merge requests.
        /// </summary>
        public string DisplayName { get; }

        /// <summary>
        /// Returns an independent proof with an explicitly selected name. Trims and truncates to
        /// 64 UTF-16 code units without splitting a surrogate pair; blank removes the name.
        /// Provider companions do not automatically copy profile names into a proof.
        /// </summary>
        public PlayServExternalIdentityProof WithDisplayName(string displayName) =>
            new PlayServExternalIdentityProof(this,
                Playserv.Runtime.Abstractions.PlayServDisplayNamePolicy.Normalize(displayName));

        private PlayServExternalIdentityProof(PlayServExternalIdentityProof source, string displayName)
        {
            ProviderId = source.ProviderId;
            IdToken = source.IdToken;
            AuthorizationCode = source.AuthorizationCode;
            ProviderToken = source.ProviderToken;
            Mode = source.Mode;
            Nonce = source.Nonce;
            DisplayName = displayName;
        }

        public bool HasIdToken => !string.IsNullOrWhiteSpace(IdToken);

        public bool HasAuthorizationCode => !string.IsNullOrWhiteSpace(AuthorizationCode);
    }
}
