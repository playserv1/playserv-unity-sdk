using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Identity;
using UnityEngine;

namespace Playserv.Wrapper
{
    internal interface IPlayServUnityDeviceInfo
    {
        string Platform { get; }

        string ApplicationIdentifier { get; }

        string DeviceUniqueIdentifier { get; }

        bool DevelopmentWarningsEnabled { get; }

        void LogWarning(string message);
    }

    internal sealed class PlayServUnityDeviceInfo : IPlayServUnityDeviceInfo
    {
        public string Platform
        {
            get
            {
                switch (Application.platform)
                {
                    case RuntimePlatform.Android:
                        return "android";
                    case RuntimePlatform.IPhonePlayer:
                        return "ios";
                    default:
                        return null;
                }
            }
        }

        public string ApplicationIdentifier => Application.identifier;

        public string DeviceUniqueIdentifier => SystemInfo.deviceUniqueIdentifier;

        public bool DevelopmentWarningsEnabled => Application.isEditor || Debug.isDebugBuild;

        public void LogWarning(string message) => Debug.LogWarning(message);
    }

    internal sealed class PlayServAutomaticPlayerFingerprintProvider : IPlayServPlayerFingerprintProvider
    {
        private const string HashDomain = "playserv-device-fingerprint-v1";
        private static int _warningLogged;
        private readonly IPlayServUnityDeviceInfo _deviceInfo;

        public PlayServAutomaticPlayerFingerprintProvider(IPlayServUnityDeviceInfo deviceInfo = null)
        {
            _deviceInfo = deviceInfo ?? new PlayServUnityDeviceInfo();
        }

        public Task<PlayServPlayerFingerprint> GetFingerprintAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var platform = _deviceInfo.Platform;
            if (string.IsNullOrWhiteSpace(platform))
                return UnsupportedAsync();

            var applicationIdentifier = _deviceInfo.ApplicationIdentifier;
            var deviceIdentifier = _deviceInfo.DeviceUniqueIdentifier;
            if (string.IsNullOrWhiteSpace(applicationIdentifier) ||
                string.IsNullOrWhiteSpace(deviceIdentifier) ||
                string.Equals(
                    deviceIdentifier,
                    SystemInfo.unsupportedIdentifier,
                    StringComparison.Ordinal))
            {
                return UnsupportedAsync();
            }

            var hash = ComputeDeviceIdHash(platform, applicationIdentifier, deviceIdentifier);
            var fingerprint = new PlayServPlayerFingerprint(
                new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["platform"] = platform,
                    ["device_id_hash"] = hash
                });
            return Task.FromResult(fingerprint);
        }

        internal static string ComputeDeviceIdHash(
            string platform,
            string applicationIdentifier,
            string deviceIdentifier)
        {
            if (string.IsNullOrWhiteSpace(platform))
                throw new ArgumentException("Platform is required.", nameof(platform));
            if (string.IsNullOrWhiteSpace(applicationIdentifier))
                throw new ArgumentException("Application identifier is required.", nameof(applicationIdentifier));
            if (string.IsNullOrWhiteSpace(deviceIdentifier))
                throw new ArgumentException("Device identifier is required.", nameof(deviceIdentifier));

            var input = string.Concat(
                HashDomain,
                "\0",
                platform.Trim(),
                "\0",
                applicationIdentifier.Trim(),
                "\0",
                deviceIdentifier.Trim());
            byte[] digest;
            using (var sha256 = SHA256.Create())
                digest = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));

            var result = new char[digest.Length * 2];
            const string hex = "0123456789abcdef";
            for (var index = 0; index < digest.Length; index++)
            {
                result[index * 2] = hex[digest[index] >> 4];
                result[index * 2 + 1] = hex[digest[index] & 0x0f];
            }
            return new string(result);
        }

        internal static void ResetWarningForTests() => Interlocked.Exchange(ref _warningLogged, 0);

        private Task<PlayServPlayerFingerprint> UnsupportedAsync()
        {
            if (_deviceInfo.DevelopmentWarningsEnabled &&
                Interlocked.Exchange(ref _warningLogged, 1) == 0)
            {
                _deviceInfo.LogWarning(
                    "PlayServ automatic device fingerprinting is unavailable on this platform. " +
                    "Configure PlayerFingerprintProvider to supply consented signals; login will continue without a fingerprint.");
            }
            return Task.FromResult<PlayServPlayerFingerprint>(null);
        }
    }
}
