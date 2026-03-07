#nullable enable

using System;
using System.IO;
using Newtonsoft.Json;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

namespace Playserv.Wrapper
{
    /// <summary>
    /// Resolves active PlayServ environment and maps JSON profiles to runtime settings.
    /// </summary>
    public static class PlayServEnvironmentResolver
    {
        public const string LocalEnvironment = "local";
        public const string DevEnvironment = "dev";
        public const string TestEnvironment = "test";
        public const string ProdEnvironment = "prod";
        public const string DefaultEnvironment = DevEnvironment;

        public const string EnvironmentVariableName = "PLAYSERV_ENV";
        public const string CommandLineArgumentName = "-playservEnv";
        public const string ConfigFileRelativePath = "Assets/Editor/PlayServEnvironments.json";

        private static readonly string[] SupportedEnvironments =
        {
            LocalEnvironment,
            DevEnvironment,
            TestEnvironment,
            ProdEnvironment
        };

        /// <summary>
        /// Ordered list of supported environments.
        /// </summary>
        public static string[] Environments => (string[])SupportedEnvironments.Clone();

        /// <summary>
        /// Resolves active environment from command line, process environment, and fallback value.
        /// </summary>
        /// <param name="fallbackEnvironment">Environment name from config file, usually activeEnvironment.</param>
        /// <returns>Normalized environment name.</returns>
        public static string ResolveEnvironmentName(string? fallbackEnvironment)
        {
            var commandLineOverride = TryReadCommandLineOverride();
            if (TryNormalizeEnvironment(commandLineOverride, out var normalized))
                return normalized;

            var processOverride = Environment.GetEnvironmentVariable(EnvironmentVariableName);
            if (TryNormalizeEnvironment(processOverride, out normalized))
                return normalized;

            if (TryNormalizeEnvironment(fallbackEnvironment, out normalized))
                return normalized;

            return DefaultEnvironment;
        }

        /// <summary>
        /// Converts user-provided environment value into one of: local/dev/test/prod.
        /// </summary>
        public static bool TryNormalizeEnvironment(string? value, out string normalized)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                normalized = string.Empty;
                return false;
            }

            switch (value.Trim().ToLowerInvariant())
            {
                case LocalEnvironment:
                case "localhost":
                    normalized = LocalEnvironment;
                    return true;
                case DevEnvironment:
                case "development":
                    normalized = DevEnvironment;
                    return true;
                case TestEnvironment:
                case "testing":
                    normalized = TestEnvironment;
                    return true;
                case ProdEnvironment:
                case "production":
                    normalized = ProdEnvironment;
                    return true;
                default:
                    normalized = string.Empty;
                    return false;
            }
        }

        /// <summary>
        /// Parses JSON config text into strongly typed environment config.
        /// </summary>
        public static bool TryParseConfig(string? json, out PlayServEnvironmentConfig config, out string error)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                config = PlayServEnvironmentConfig.CreateDefault();
                error = "PlayServ environment JSON is empty.";
                return false;
            }

            try
            {
                var deserialized = JsonConvert.DeserializeObject<PlayServEnvironmentConfig>(json);
                config = deserialized ?? PlayServEnvironmentConfig.CreateDefault();
                error = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                config = PlayServEnvironmentConfig.CreateDefault();
                error = $"Failed to parse PlayServ environments JSON: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Serializes environment config with stable formatting.
        /// </summary>
        public static string ToJson(PlayServEnvironmentConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            return JsonConvert.SerializeObject(config, Formatting.Indented);
        }

#if UNITY_EDITOR
        /// <summary>
        /// Resolves absolute file path for environment config.
        /// </summary>
        public static string GetConfigFilePath()
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.GetFullPath(Path.Combine(projectRoot, ConfigFileRelativePath));
        }

        /// <summary>
        /// Loads environment config from Assets/Editor/PlayServEnvironments.json.
        /// </summary>
        public static bool TryLoadConfigFromFile(out PlayServEnvironmentConfig config, out string error)
        {
            var filePath = GetConfigFilePath();
            if (!File.Exists(filePath))
            {
                config = PlayServEnvironmentConfig.CreateDefault();
                error = $"{ConfigFileRelativePath} was not found.";
                return false;
            }

            var json = File.ReadAllText(filePath);
            return TryParseConfig(json, out config, out error);
        }

        /// <summary>
        /// Resolves profile from environment JSON file and applies it on top of provided settings.
        /// </summary>
        public static bool TryResolveSettingsFromFile(
            PlayServSettings baseSettings,
            out PlayServSettings resolvedSettings,
            out string selectedEnvironment,
            out string error)
        {
            if (baseSettings == null)
                throw new ArgumentNullException(nameof(baseSettings));

            resolvedSettings = baseSettings.Clone();
            selectedEnvironment = ResolveEnvironmentName(null);

            if (!TryLoadConfigFromFile(out var config, out error))
                return false;

            selectedEnvironment = ResolveEnvironmentName(config.ActiveEnvironment);
            if (!config.TryGetProfile(selectedEnvironment, out var profile))
            {
                error = $"Environment '{selectedEnvironment}' is missing in {ConfigFileRelativePath}.";
                return false;
            }

            profile.ApplyTo(resolvedSettings);
            error = string.Empty;
            return true;
        }

        /// <summary>
        /// Resolves effective settings for editor workflows.
        /// </summary>
        public static PlayServSettings ResolveSettingsForEditor(
            PlayServConfig config,
            out string selectedEnvironment,
            out bool profileApplied,
            out string error)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            var baseSettings = config.ToSettings();
            if (TryResolveSettingsFromFile(baseSettings, out var resolved, out selectedEnvironment, out error))
            {
                profileApplied = true;
                return resolved;
            }

            selectedEnvironment = ResolveEnvironmentName(null);
            profileApplied = false;
            return baseSettings;
        }
#endif

        private static string? TryReadCommandLineOverride()
        {
            try
            {
                var args = Environment.GetCommandLineArgs();
                if (args == null || args.Length == 0)
                    return null;

                for (var i = 0; i < args.Length; i++)
                {
                    var arg = args[i];
                    if (string.IsNullOrWhiteSpace(arg))
                        continue;

                    if (arg.StartsWith(CommandLineArgumentName + "=", StringComparison.OrdinalIgnoreCase))
                        return arg.Substring(CommandLineArgumentName.Length + 1).Trim();

                    if (string.Equals(arg, CommandLineArgumentName, StringComparison.OrdinalIgnoreCase) &&
                        i + 1 < args.Length)
                    {
                        return args[i + 1]?.Trim();
                    }
                }
            }
            catch
            {
                // Ignore command line parse failures and fallback to defaults.
            }

            return null;
        }

    }

    [Serializable]
    public sealed class PlayServEnvironmentConfig
    {
        [JsonProperty("activeEnvironment")]
        public string ActiveEnvironment { get; set; } = PlayServEnvironmentResolver.DefaultEnvironment;

        [JsonProperty("environments")]
        public PlayServEnvironmentSections Environments { get; set; } = new();

        public bool TryGetProfile(string environmentName, out PlayServEnvironmentProfile profile)
        {
            profile = null!;

            if (!PlayServEnvironmentResolver.TryNormalizeEnvironment(environmentName, out var normalized))
                return false;

            if (Environments == null)
                return false;

            switch (normalized)
            {
                case PlayServEnvironmentResolver.LocalEnvironment:
                    profile = Environments.Local;
                    break;
                case PlayServEnvironmentResolver.DevEnvironment:
                    profile = Environments.Dev;
                    break;
                case PlayServEnvironmentResolver.TestEnvironment:
                    profile = Environments.Test;
                    break;
                case PlayServEnvironmentResolver.ProdEnvironment:
                    profile = Environments.Prod;
                    break;
                default:
                    return false;
            }

            return profile != null;
        }

        public static PlayServEnvironmentConfig CreateDefault()
        {
            return new PlayServEnvironmentConfig
            {
                ActiveEnvironment = PlayServEnvironmentResolver.DefaultEnvironment,
                Environments = new PlayServEnvironmentSections
                {
                    Local = PlayServEnvironmentProfile.CreateLocalDefaults(),
                    Dev = PlayServEnvironmentProfile.CreateDevDefaults(),
                    Test = PlayServEnvironmentProfile.CreateTestDefaults(),
                    Prod = PlayServEnvironmentProfile.CreateProdDefaults()
                }
            };
        }
    }

    [Serializable]
    public sealed class PlayServEnvironmentSections
    {
        [JsonProperty("local")]
        public PlayServEnvironmentProfile Local { get; set; } = PlayServEnvironmentProfile.CreateLocalDefaults();

        [JsonProperty("dev")]
        public PlayServEnvironmentProfile Dev { get; set; } = PlayServEnvironmentProfile.CreateDevDefaults();

        [JsonProperty("test")]
        public PlayServEnvironmentProfile Test { get; set; } = PlayServEnvironmentProfile.CreateTestDefaults();

        [JsonProperty("prod")]
        public PlayServEnvironmentProfile Prod { get; set; } = PlayServEnvironmentProfile.CreateProdDefaults();
    }

    [Serializable]
    public sealed class PlayServEnvironmentProfile
    {
        [JsonProperty("gameAccessToken")]
        public string? GameAccessToken { get; set; }

        [JsonProperty("gameId")]
        public string? GameId { get; set; }

        [JsonProperty("userId")]
        public string? UserId { get; set; }

        [JsonProperty("gameVersion")]
        public string? GameVersion { get; set; }

        [JsonProperty("sdkVersion")]
        public string? SdkVersion { get; set; }

        [JsonProperty("allowMultipleConnections")]
        public bool? AllowMultipleConnections { get; set; }

        [JsonProperty("keepAlivePingIntervalMs")]
        public int? KeepAlivePingIntervalMs { get; set; }

        [JsonProperty("keepAlivePongTimeoutMs")]
        public int? KeepAlivePongTimeoutMs { get; set; }

        [JsonProperty("networkTransformSyncIntervalMs")]
        public int? NetworkTransformSyncIntervalMs { get; set; }

        [JsonProperty("backendServerAddress")]
        public string? BackendServerAddress { get; set; }

        [JsonProperty("deployApiServerAddress")]
        public string? DeployApiServerAddress { get; set; }

        [JsonProperty("schemaApiServerAddress")]
        public string? SchemaApiServerAddress { get; set; }

        [JsonProperty("deployAuthToken")]
        public string? DeployAuthToken { get; set; }

        [JsonProperty("timeoutSeconds")]
        public int? TimeoutSeconds { get; set; }

        public void ApplyTo(PlayServSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            if (GameAccessToken != null)
                settings.GameAccessToken = GameAccessToken;
            if (GameId != null)
                settings.GameId = GameId;
            if (UserId != null)
                settings.UserId = UserId;
            if (GameVersion != null)
                settings.GameVersion = GameVersion;
            if (SdkVersion != null)
                settings.SdkVersion = SdkVersion;
            if (AllowMultipleConnections.HasValue)
                settings.AllowMultipleConnections = AllowMultipleConnections.Value;
            if (KeepAlivePingIntervalMs.HasValue)
                settings.KeepAlivePingIntervalMs = KeepAlivePingIntervalMs.Value;
            if (KeepAlivePongTimeoutMs.HasValue)
                settings.KeepAlivePongTimeoutMs = KeepAlivePongTimeoutMs.Value;
            if (NetworkTransformSyncIntervalMs.HasValue)
                settings.NetworkTransformSyncIntervalMs = NetworkTransformSyncIntervalMs.Value;
            if (BackendServerAddress != null)
                settings.BackendServerAddress = BackendServerAddress;
            if (DeployApiServerAddress != null)
                settings.DeployApiServerAddress = DeployApiServerAddress;
            if (SchemaApiServerAddress != null)
                settings.SchemaApiServerAddress = SchemaApiServerAddress;
            if (DeployAuthToken != null)
                settings.DeployAuthToken = DeployAuthToken;
            if (TimeoutSeconds.HasValue)
                settings.TimeoutSeconds = TimeoutSeconds.Value;
        }

        public static PlayServEnvironmentProfile CreateLocalDefaults()
        {
            return new PlayServEnvironmentProfile
            {
                BackendServerAddress = "ws://localhost:8080/ws/",
                DeployApiServerAddress = "http://localhost:8080",
                SchemaApiServerAddress = "http://localhost:8081",
                KeepAlivePingIntervalMs = 30000,
                KeepAlivePongTimeoutMs = 10000,
                NetworkTransformSyncIntervalMs = 100,
                AllowMultipleConnections = true,
                TimeoutSeconds = 120
            };
        }

        public static PlayServEnvironmentProfile CreateDevDefaults()
        {
            return new PlayServEnvironmentProfile
            {
                BackendServerAddress = "wss://proxy.dev.playserv.io/ws",
                DeployApiServerAddress = "https://deployment.dev.playserv.io",
                SchemaApiServerAddress = "https://backoffice.dev.playserv.io",
                KeepAlivePingIntervalMs = 30000,
                KeepAlivePongTimeoutMs = 10000,
                NetworkTransformSyncIntervalMs = 100,
                AllowMultipleConnections = true,
                TimeoutSeconds = 120
            };
        }

        public static PlayServEnvironmentProfile CreateTestDefaults()
        {
            return new PlayServEnvironmentProfile
            {
                BackendServerAddress = "wss://playserv-proxy.test.playserv.io/ws",
                DeployApiServerAddress = "http://playserv-deployment.test.playserv.io",
                SchemaApiServerAddress = "https://playserv-backoffice.test.playserv.io",
                KeepAlivePingIntervalMs = 30000,
                KeepAlivePongTimeoutMs = 10000,
                NetworkTransformSyncIntervalMs = 100,
                AllowMultipleConnections = true,
                TimeoutSeconds = 120
            };
        }

        public static PlayServEnvironmentProfile CreateProdDefaults()
        {
            return new PlayServEnvironmentProfile
            {
                BackendServerAddress = "wss://proxy.playserv.io",
                DeployApiServerAddress = "https://deployment.playserv.io",
                SchemaApiServerAddress = "https://backoffice.playserv.io",
                KeepAlivePingIntervalMs = 30000,
                KeepAlivePongTimeoutMs = 10000,
                NetworkTransformSyncIntervalMs = 100,
                AllowMultipleConnections = true,
                TimeoutSeconds = 120
            };
        }
    }
}

#nullable restore
