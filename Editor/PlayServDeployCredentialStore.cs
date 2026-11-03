using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Playserv.Editor
{
    /// <summary>
    /// Resolves deployment credentials from process environment or project-scoped local editor storage.
    /// </summary>
    public static class PlayServDeployCredentialStore
    {
        public const string EnvironmentVariableName = "PLAYSERV_DEPLOY_AUTH_TOKEN";

        private const string LocalPreferencePrefix = "PlayServ.DeployAuthToken.";

        public static bool HasEnvironmentToken =>
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvironmentVariableName));

        public static string GetLocalToken()
        {
            return EditorPrefs.GetString(GetLocalPreferenceKey(), string.Empty);
        }

        public static void SetLocalToken(string value)
        {
            var normalized = Normalize(value);
            if (normalized.Length == 0)
            {
                ClearLocalToken();
                return;
            }

            EditorPrefs.SetString(GetLocalPreferenceKey(), normalized);
        }

        public static void ClearLocalToken()
        {
            EditorPrefs.DeleteKey(GetLocalPreferenceKey());
        }

        public static string ResolveToken()
        {
            var environmentToken = Normalize(Environment.GetEnvironmentVariable(EnvironmentVariableName));
            return environmentToken.Length > 0 ? environmentToken : Normalize(GetLocalToken());
        }

        internal static bool MigrateLegacySecrets(Wrapper.PlayServConfig config)
        {
            if (config == null ||
                !config.ConsumeLegacySecrets(out var authorization, out var deployAuthToken))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(deployAuthToken) &&
                !HasEnvironmentToken &&
                string.IsNullOrWhiteSpace(GetLocalToken()))
            {
                SetLocalToken(deployAuthToken);
                Debug.Log(
                    "[PlayServ] Moved the legacy deploy token from PlayServConfig to project-scoped local Editor storage.");
            }

            if (!string.IsNullOrWhiteSpace(authorization))
            {
                var requiresRotation = ContainsSecretKey(authorization)
                    ? " The removed value contains an sk_* key; revoke and rotate it."
                    : string.Empty;
                Debug.LogWarning(
                    "[PlayServ] Removed serialized runtime authorization from PlayServConfig. " +
                    "Configure a player JWT through PlayServ.SetRuntimeTokenProvider()." +
                    requiresRotation);
            }

            return true;
        }

        private static string GetLocalPreferenceKey()
        {
            var projectPath = Path.GetFullPath(Application.dataPath ?? string.Empty);
            return LocalPreferencePrefix + Hash128.Compute(projectPath).ToString();
        }

        private static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var normalized = value.Trim();
            const string bearerPrefix = "Bearer ";
            if (normalized.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
                normalized = normalized.Substring(bearerPrefix.Length).Trim();

            if (normalized.IndexOf('\r') >= 0 || normalized.IndexOf('\n') >= 0)
                throw new InvalidOperationException("PlayServ deploy tokens cannot contain line breaks.");

            return normalized;
        }

        private static bool ContainsSecretKey(string value)
        {
            var normalized = value?.Trim() ?? string.Empty;
            const string bearerPrefix = "Bearer ";
            if (normalized.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
                normalized = normalized.Substring(bearerPrefix.Length).Trim();

            return normalized.StartsWith("sk_", StringComparison.OrdinalIgnoreCase);
        }
    }
}
