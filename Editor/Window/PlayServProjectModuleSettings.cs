using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Playserv.Modules;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Playserv.Editor
{
    internal static class PlayServProjectModuleSettings
    {
        public const int CurrentSchemaVersion = 1;
        public const string CustomProfileId = "custom";
        public const string ProjectRelativePath = "ProjectSettings/PlayServModules.json";
        public const string DeploymentEditorToolId = "deployment";
        public const string ModelSyncEditorToolId = "model-sync";
        public const string CodegenEditorToolId = "codegen";
        public const string ModuleStressTestsEditorToolId = "module-stress-tests";

        private static readonly string[] KnownEditorToolIds =
        {
            DeploymentEditorToolId,
            ModelSyncEditorToolId,
            CodegenEditorToolId,
            ModuleStressTestsEditorToolId
        };

        public static string CurrentBuildTargetGroupId =>
            EditorUserBuildSettings.selectedBuildTargetGroup.ToString();

        public static string CurrentProfileId
        {
            get
            {
                var data = LoadOrCreate();
                var platformOverride = FindPlatformOverride(data, CurrentBuildTargetGroupId);
                return NormalizeProfileId(platformOverride?.profileId ?? data.activeProfileId);
            }
        }

        public static bool HasCurrentPlatformOverride =>
            FindPlatformOverride(LoadOrCreate(), CurrentBuildTargetGroupId) != null;

        public static bool IsEditorToolEnabled(string editorToolId, bool defaultEnabled)
        {
            if (string.IsNullOrWhiteSpace(editorToolId))
                return defaultEnabled;

            var data = LoadOrCreate();
            return (data.enabledEditorToolIds ?? Array.Empty<string>())
                .Contains(editorToolId, StringComparer.Ordinal);
        }

        public static bool SetEditorToolEnabled(string editorToolId, bool enabled)
        {
            if (!KnownEditorToolIds.Contains(editorToolId, StringComparer.Ordinal))
                throw new ArgumentException($"Unknown PlayServ editor tool id: {editorToolId}", nameof(editorToolId));

            var data = LoadOrCreate();
            var enabledEditorToolIds =
                new HashSet<string>(data.enabledEditorToolIds ?? Array.Empty<string>(), StringComparer.Ordinal);
            var changed = enabled
                ? enabledEditorToolIds.Add(editorToolId)
                : enabledEditorToolIds.Remove(editorToolId);

            if (!changed)
                return false;

            data.enabledEditorToolIds = enabledEditorToolIds.ToArray();
            WriteData(data);
            return true;
        }

        public static PlayServRuntimeModuleState LoadEffectiveState()
        {
            var data = LoadOrCreate();
            if (ReconcileDiscoveredModules(data))
                WriteData(data);

            var platformOverride = FindPlatformOverride(data, CurrentBuildTargetGroupId);
            var enabledModuleIds = platformOverride?.enabledModuleIds ?? data.enabledModuleIds;
            return CreateState(enabledModuleIds);
        }

        public static bool SaveEffectiveState(PlayServRuntimeModuleState state, string profileId = null)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            var data = LoadOrCreate();
            ReconcileDiscoveredModules(data);
            var platformOverride = FindPlatformOverride(data, CurrentBuildTargetGroupId);
            var existingEnabledModuleIds = platformOverride?.enabledModuleIds ?? data.enabledModuleIds;
            var enabledModuleIds = PreserveUnavailableEnabledModuleIds(existingEnabledModuleIds, state);
            var resolvedProfileId = NormalizeProfileId(
                profileId ?? platformOverride?.profileId ?? data.activeProfileId);

            if (platformOverride != null)
            {
                if (string.Equals(platformOverride.profileId, resolvedProfileId, StringComparison.Ordinal) &&
                    SequenceEqual(platformOverride.enabledModuleIds, enabledModuleIds))
                {
                    return false;
                }

                platformOverride.profileId = resolvedProfileId;
                platformOverride.enabledModuleIds = enabledModuleIds;
            }
            else
            {
                if (string.Equals(data.activeProfileId, resolvedProfileId, StringComparison.Ordinal) &&
                    SequenceEqual(data.enabledModuleIds, enabledModuleIds))
                {
                    return false;
                }

                data.activeProfileId = resolvedProfileId;
                data.enabledModuleIds = enabledModuleIds;
            }

            UpdateKnownModuleIds(data);
            WriteData(data);
            return true;
        }

        public static bool CreateCurrentPlatformOverride()
        {
            var buildTargetGroupId = CurrentBuildTargetGroupId;
            if (string.Equals(buildTargetGroupId, BuildTargetGroup.Unknown.ToString(), StringComparison.Ordinal))
                return false;

            var data = LoadOrCreate();
            if (FindPlatformOverride(data, buildTargetGroupId) != null)
                return false;

            ReconcileDiscoveredModules(data);
            var platformOverride = new PlatformOverrideData
            {
                buildTargetGroup = buildTargetGroupId,
                profileId = NormalizeProfileId(data.activeProfileId),
                enabledModuleIds = Clone(data.enabledModuleIds)
            };

            var overrides = new List<PlatformOverrideData>(data.platformOverrides ?? Array.Empty<PlatformOverrideData>())
            {
                platformOverride
            };
            data.platformOverrides = overrides
                .OrderBy(item => item.buildTargetGroup, StringComparer.Ordinal)
                .ToArray();

            WriteData(data);
            return true;
        }

        public static bool ClearCurrentPlatformOverride()
        {
            var data = LoadOrCreate();
            var buildTargetGroupId = CurrentBuildTargetGroupId;
            var remaining = (data.platformOverrides ?? Array.Empty<PlatformOverrideData>())
                .Where(item => item != null &&
                               !string.Equals(item.buildTargetGroup, buildTargetGroupId, StringComparison.Ordinal))
                .ToArray();

            if (remaining.Length == (data.platformOverrides ?? Array.Empty<PlatformOverrideData>()).Length)
                return false;

            data.platformOverrides = remaining;
            WriteData(data);
            return true;
        }

        public static bool Repair()
        {
            if (!TryReadData(out var data, out _))
            {
                data = File.Exists(AbsolutePath)
                    ? CreateDefaultData()
                    : CreateFromLegacyPreferences();
                WriteData(data);
                PlayServRuntimeModuleDefines.ClearLegacyUserPreferences();
                ClearLegacyEditorToolPreferences();
                return true;
            }

            if (data.schemaVersion != CurrentSchemaVersion)
                return false;

            var before = JsonUtility.ToJson(data);
            NormalizeForWrite(data);
            ReconcileDiscoveredModules(data);
            var after = JsonUtility.ToJson(data);
            if (string.Equals(before, after, StringComparison.Ordinal))
                return false;

            WriteData(data);
            return true;
        }

        public static IReadOnlyList<PlayServProjectModuleSettingsDiagnostic> Validate()
        {
            var diagnostics = new List<PlayServProjectModuleSettingsDiagnostic>();
            if (!File.Exists(AbsolutePath))
            {
                diagnostics.Add(PlayServProjectModuleSettingsDiagnostic.Error(
                    $"Project module settings file is missing: {ProjectRelativePath}."));
                return diagnostics;
            }

            if (!TryReadData(out var data, out var error))
            {
                diagnostics.Add(PlayServProjectModuleSettingsDiagnostic.Error(error));
                return diagnostics;
            }

            if (data.schemaVersion != CurrentSchemaVersion)
            {
                diagnostics.Add(PlayServProjectModuleSettingsDiagnostic.Error(
                    $"Unsupported project module settings schemaVersion {data.schemaVersion}. Expected {CurrentSchemaVersion}."));
            }

            ValidateProfileId(data.activeProfileId, "base configuration", diagnostics);
            ValidateModuleIds(data.enabledModuleIds, "base configuration", diagnostics);
            ValidateDuplicateValues(data.enabledModuleIds, "base enabled module id", diagnostics);
            ValidateDuplicateValues(data.knownModuleIds, "known module id", diagnostics);
            ValidateEditorToolIds(data.enabledEditorToolIds, diagnostics);
            ValidateDuplicateValues(data.enabledEditorToolIds, "enabled editor tool id", diagnostics);

            var buildTargetGroups = new HashSet<string>(StringComparer.Ordinal);
            foreach (var platformOverride in data.platformOverrides ?? Array.Empty<PlatformOverrideData>())
            {
                if (platformOverride == null)
                {
                    diagnostics.Add(PlayServProjectModuleSettingsDiagnostic.Error(
                        "Project module settings contains a null platform override."));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(platformOverride.buildTargetGroup))
                {
                    diagnostics.Add(PlayServProjectModuleSettingsDiagnostic.Error(
                        "Platform override buildTargetGroup is empty."));
                }
                else
                {
                    if (!buildTargetGroups.Add(platformOverride.buildTargetGroup))
                    {
                        diagnostics.Add(PlayServProjectModuleSettingsDiagnostic.Error(
                            $"Duplicate platform override: {platformOverride.buildTargetGroup}."));
                    }

                    if (!Enum.TryParse(platformOverride.buildTargetGroup, out BuildTargetGroup _))
                    {
                        diagnostics.Add(PlayServProjectModuleSettingsDiagnostic.Warning(
                            $"Unknown platform override buildTargetGroup: {platformOverride.buildTargetGroup}."));
                    }
                }

                var scope = $"platform override {platformOverride.buildTargetGroup}";
                ValidateProfileId(platformOverride.profileId, scope, diagnostics);
                ValidateModuleIds(platformOverride.enabledModuleIds, scope, diagnostics);
                ValidateDuplicateValues(platformOverride.enabledModuleIds, $"{scope} enabled module id", diagnostics);
            }

            return diagnostics;
        }

        internal static DateTime GetLastWriteTimeUtc()
        {
            return File.Exists(AbsolutePath)
                ? File.GetLastWriteTimeUtc(AbsolutePath)
                : DateTime.MinValue;
        }

        private static string AbsolutePath
        {
            get
            {
                var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
                return Path.Combine(projectRoot ?? string.Empty, ProjectRelativePath);
            }
        }

        private static SettingsData LoadOrCreate()
        {
            if (TryReadData(out var data, out var error))
            {
                if (data.schemaVersion != CurrentSchemaVersion)
                {
                    throw new InvalidDataException(
                        $"Unsupported {ProjectRelativePath} schemaVersion {data.schemaVersion}. " +
                        $"Expected {CurrentSchemaVersion}. Update the PlayServ SDK or repair the file with a compatible SDK version.");
                }

                NormalizeAfterRead(data);
                return data;
            }

            if (File.Exists(AbsolutePath))
                throw new InvalidDataException(error);

            data = CreateFromLegacyPreferences();
            WriteData(data);
            PlayServRuntimeModuleDefines.ClearLegacyUserPreferences();
            ClearLegacyEditorToolPreferences();
            return data;
        }

        private static SettingsData CreateFromLegacyPreferences()
        {
            var state = PlayServRuntimeModuleDefines.LoadLegacyUserPreferenceState();
            return new SettingsData
            {
                schemaVersion = CurrentSchemaVersion,
                activeProfileId = DetectProfileId(state),
                enabledModuleIds = GetEnabledModuleIds(state),
                enabledEditorToolIds = GetLegacyEnabledEditorToolIds(),
                knownModuleIds = PlayServModuleManifest.RuntimeModules
                    .Select(module => module.Id)
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .ToArray(),
                platformOverrides = Array.Empty<PlatformOverrideData>()
            };
        }

        private static SettingsData CreateDefaultData()
        {
            var state = CreateProfileState(PlayServSdkProfiles.ClientSdk);
            return new SettingsData
            {
                schemaVersion = CurrentSchemaVersion,
                activeProfileId = PlayServSdkProfiles.ClientSdkId,
                enabledModuleIds = GetEnabledModuleIds(state),
                enabledEditorToolIds = new[]
                {
                    DeploymentEditorToolId,
                    ModelSyncEditorToolId,
                    CodegenEditorToolId
                },
                knownModuleIds = PlayServModuleManifest.RuntimeModules
                    .Select(module => module.Id)
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .ToArray(),
                platformOverrides = Array.Empty<PlatformOverrideData>()
            };
        }

        private static bool TryReadData(out SettingsData data, out string error)
        {
            data = null;
            error = string.Empty;
            if (!File.Exists(AbsolutePath))
                return false;

            try
            {
                data = JsonUtility.FromJson<SettingsData>(File.ReadAllText(AbsolutePath));
                if (data == null)
                {
                    error = $"Project module settings is empty or invalid: {ProjectRelativePath}.";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = $"Could not read {ProjectRelativePath}: {ex.GetBaseException().Message}";
                return false;
            }
        }

        private static void WriteData(SettingsData data)
        {
            NormalizeForWrite(data);
            var directory = Path.GetDirectoryName(AbsolutePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(AbsolutePath, JsonUtility.ToJson(data, prettyPrint: true) + Environment.NewLine);
            PlayServProjectModuleSettingsWatcher.NotifyWritten();
        }

        private static void NormalizeAfterRead(SettingsData data)
        {
            data.activeProfileId = NormalizeProfileId(data.activeProfileId);
            data.enabledModuleIds = data.enabledModuleIds ?? Array.Empty<string>();
            data.enabledEditorToolIds = data.enabledEditorToolIds ?? Array.Empty<string>();
            data.knownModuleIds = data.knownModuleIds ?? Array.Empty<string>();
            data.platformOverrides = data.platformOverrides ?? Array.Empty<PlatformOverrideData>();
            for (var i = 0; i < data.platformOverrides.Length; i++)
            {
                var platformOverride = data.platformOverrides[i];
                if (platformOverride == null)
                    continue;

                platformOverride.buildTargetGroup = (platformOverride.buildTargetGroup ?? string.Empty).Trim();
                platformOverride.profileId = NormalizeProfileId(platformOverride.profileId);
                platformOverride.enabledModuleIds = platformOverride.enabledModuleIds ?? Array.Empty<string>();
            }
        }

        private static void NormalizeForWrite(SettingsData data)
        {
            NormalizeAfterRead(data);
            data.schemaVersion = CurrentSchemaVersion;
            data.enabledModuleIds = NormalizeIds(data.enabledModuleIds);
            data.enabledEditorToolIds = NormalizeIds(data.enabledEditorToolIds);
            data.knownModuleIds = NormalizeIds(data.knownModuleIds);
            data.platformOverrides = data.platformOverrides
                .Where(item => item != null)
                .OrderBy(item => item.buildTargetGroup, StringComparer.Ordinal)
                .ToArray();

            for (var i = 0; i < data.platformOverrides.Length; i++)
                data.platformOverrides[i].enabledModuleIds = NormalizeIds(data.platformOverrides[i].enabledModuleIds);
        }

        private static bool ReconcileDiscoveredModules(SettingsData data)
        {
            var changed = false;
            var knownModuleIds = new HashSet<string>(data.knownModuleIds ?? Array.Empty<string>(), StringComparer.Ordinal);
            var baseEnabledModuleIds = new HashSet<string>(data.enabledModuleIds ?? Array.Empty<string>(), StringComparer.Ordinal);
            var platformEnabledModuleIds = new Dictionary<PlatformOverrideData, HashSet<string>>();

            foreach (var platformOverride in data.platformOverrides ?? Array.Empty<PlatformOverrideData>())
            {
                if (platformOverride != null)
                {
                    platformEnabledModuleIds[platformOverride] =
                        new HashSet<string>(platformOverride.enabledModuleIds ?? Array.Empty<string>(), StringComparer.Ordinal);
                }
            }

            foreach (var module in PlayServModuleManifest.RuntimeModules)
            {
                if (knownModuleIds.Contains(module.Id))
                    continue;

                knownModuleIds.Add(module.Id);
                changed = true;

                if (ShouldEnableNewModule(data.activeProfileId, module))
                    baseEnabledModuleIds.Add(module.Id);

                foreach (var pair in platformEnabledModuleIds)
                {
                    if (ShouldEnableNewModule(pair.Key.profileId, module))
                        pair.Value.Add(module.Id);
                }
            }

            if (!changed)
                return false;

            data.knownModuleIds = knownModuleIds.OrderBy(id => id, StringComparer.Ordinal).ToArray();
            data.enabledModuleIds = baseEnabledModuleIds.OrderBy(id => id, StringComparer.Ordinal).ToArray();
            foreach (var pair in platformEnabledModuleIds)
                pair.Key.enabledModuleIds = pair.Value.OrderBy(id => id, StringComparer.Ordinal).ToArray();

            return true;
        }

        private static void UpdateKnownModuleIds(SettingsData data)
        {
            data.knownModuleIds = (data.knownModuleIds ?? Array.Empty<string>())
                .Concat(PlayServModuleManifest.RuntimeModules.Select(module => module.Id))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
        }

        private static bool ShouldEnableNewModule(string profileId, PlayServModuleManifestEntry module)
        {
            if (PlayServSdkProfiles.TryGet(profileId, out var profile))
                return IsProfileModuleEnabled(profile, module);

            return module.DefaultEnabled;
        }

        private static PlayServRuntimeModuleState CreateState(IEnumerable<string> enabledModuleIds)
        {
            var enabled = new HashSet<string>(enabledModuleIds ?? Array.Empty<string>(), StringComparer.Ordinal);
            var state = new PlayServRuntimeModuleState();
            foreach (var module in PlayServModuleManifest.RuntimeModules)
                state.SetEnabled(module.Id, enabled.Contains(module.Id));

            PlayServRuntimeModuleDefines.NormalizeDependencies(state);
            return state;
        }

        private static PlayServRuntimeModuleState CreateProfileState(PlayServSdkProfile profile)
        {
            var state = new PlayServRuntimeModuleState();
            foreach (var module in PlayServModuleManifest.RuntimeModules)
                state.SetEnabled(module.Id, IsProfileModuleEnabled(profile, module));

            PlayServRuntimeModuleDefines.NormalizeDependencies(state);
            return state;
        }

        private static bool IsProfileModuleEnabled(PlayServSdkProfile profile, PlayServModuleManifestEntry module)
        {
            if (module.ProfileIds.Length > 0)
                return module.ProfileIds.Contains(profile.Id, StringComparer.Ordinal);

            return profile.EnablesModule(module.Id);
        }

        private static string DetectProfileId(PlayServRuntimeModuleState state)
        {
            foreach (var profile in PlayServSdkProfiles.All)
            {
                if (state.HasSameEnabledModules(CreateProfileState(profile)))
                    return profile.Id;
            }

            return CustomProfileId;
        }

        private static string[] GetEnabledModuleIds(PlayServRuntimeModuleState state)
        {
            return PlayServModuleManifest.RuntimeModules
                .Where(module => state.IsEnabled(module.Id))
                .Select(module => module.Id)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
        }

        private static string[] PreserveUnavailableEnabledModuleIds(
            IEnumerable<string> existingEnabledModuleIds,
            PlayServRuntimeModuleState state)
        {
            var availableModuleIds = new HashSet<string>(
                PlayServModuleManifest.RuntimeModules.Select(module => module.Id),
                StringComparer.Ordinal);
            var unavailableEnabledModuleIds = (existingEnabledModuleIds ?? Array.Empty<string>())
                .Where(moduleId => !availableModuleIds.Contains(moduleId));

            return GetEnabledModuleIds(state)
                .Concat(unavailableEnabledModuleIds)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(moduleId => moduleId, StringComparer.Ordinal)
                .ToArray();
        }

        private static PlatformOverrideData FindPlatformOverride(SettingsData data, string buildTargetGroup)
        {
            if (data?.platformOverrides == null || string.IsNullOrWhiteSpace(buildTargetGroup))
                return null;

            for (var i = 0; i < data.platformOverrides.Length; i++)
            {
                var platformOverride = data.platformOverrides[i];
                if (platformOverride != null &&
                    string.Equals(platformOverride.buildTargetGroup, buildTargetGroup, StringComparison.Ordinal))
                {
                    return platformOverride;
                }
            }

            return null;
        }

        private static string NormalizeProfileId(string profileId)
        {
            return string.IsNullOrWhiteSpace(profileId) ? CustomProfileId : profileId.Trim();
        }

        private static string[] NormalizeIds(IEnumerable<string> values)
        {
            return (values ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
        }

        private static string[] Clone(string[] values)
        {
            return values == null || values.Length == 0
                ? Array.Empty<string>()
                : (string[])values.Clone();
        }

        private static bool SequenceEqual(string[] left, string[] right)
        {
            left = NormalizeIds(left);
            right = NormalizeIds(right);
            return left.SequenceEqual(right, StringComparer.Ordinal);
        }

        private static void ValidateProfileId(
            string profileId,
            string scope,
            ICollection<PlayServProjectModuleSettingsDiagnostic> diagnostics)
        {
            profileId = NormalizeProfileId(profileId);
            if (!string.Equals(profileId, CustomProfileId, StringComparison.Ordinal) &&
                !PlayServSdkProfiles.TryGet(profileId, out _))
            {
                diagnostics.Add(PlayServProjectModuleSettingsDiagnostic.Error(
                    $"Unknown SDK profile '{profileId}' in {scope}."));
            }
        }

        private static void ValidateModuleIds(
            IEnumerable<string> moduleIds,
            string scope,
            ICollection<PlayServProjectModuleSettingsDiagnostic> diagnostics)
        {
            foreach (var moduleId in moduleIds ?? Array.Empty<string>())
            {
                if (!PlayServModuleManifest.TryGet(moduleId, out _))
                {
                    diagnostics.Add(PlayServProjectModuleSettingsDiagnostic.Warning(
                        $"Unknown module id '{moduleId}' in {scope}. The module package may be missing."));
                }
            }
        }

        private static void ValidateEditorToolIds(
            IEnumerable<string> editorToolIds,
            ICollection<PlayServProjectModuleSettingsDiagnostic> diagnostics)
        {
            foreach (var editorToolId in editorToolIds ?? Array.Empty<string>())
            {
                if (!KnownEditorToolIds.Contains(editorToolId, StringComparer.Ordinal))
                {
                    diagnostics.Add(PlayServProjectModuleSettingsDiagnostic.Warning(
                        $"Unknown enabled editor tool id: {editorToolId}."));
                }
            }
        }

        private static string[] GetLegacyEnabledEditorToolIds()
        {
            var enabled = new List<string>();
            if (EditorPrefs.GetBool(Const.PrefModuleDeployment, true))
                enabled.Add(DeploymentEditorToolId);
            if (EditorPrefs.GetBool(Const.PrefModuleModelSync, true))
                enabled.Add(ModelSyncEditorToolId);
            if (EditorPrefs.GetBool(Const.PrefModuleCodegen, true))
                enabled.Add(CodegenEditorToolId);
            if (EditorPrefs.GetBool(Const.PrefModuleStressTests, false))
                enabled.Add(ModuleStressTestsEditorToolId);

            return enabled.ToArray();
        }

        private static void ClearLegacyEditorToolPreferences()
        {
            EditorPrefs.DeleteKey(Const.PrefModuleDeployment);
            EditorPrefs.DeleteKey(Const.PrefModuleModelSync);
            EditorPrefs.DeleteKey(Const.PrefModuleCodegen);
            EditorPrefs.DeleteKey(Const.PrefModuleStressTests);
        }

        private static void ValidateDuplicateValues(
            IEnumerable<string> values,
            string label,
            ICollection<PlayServProjectModuleSettingsDiagnostic> diagnostics)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in values ?? Array.Empty<string>())
            {
                if (!seen.Add(value ?? string.Empty))
                    diagnostics.Add(PlayServProjectModuleSettingsDiagnostic.Warning($"Duplicate {label}: {value}."));
            }
        }

        [Serializable]
        private sealed class SettingsData
        {
            public int schemaVersion = CurrentSchemaVersion;
            public string activeProfileId = PlayServSdkProfiles.ClientSdkId;
            public string[] enabledModuleIds = Array.Empty<string>();
            public string[] enabledEditorToolIds = Array.Empty<string>();
            public string[] knownModuleIds = Array.Empty<string>();
            public PlatformOverrideData[] platformOverrides = Array.Empty<PlatformOverrideData>();
        }

        [Serializable]
        private sealed class PlatformOverrideData
        {
            public string buildTargetGroup = string.Empty;
            public string profileId = CustomProfileId;
            public string[] enabledModuleIds = Array.Empty<string>();
        }
    }

    internal sealed class PlayServProjectModuleSettingsDiagnostic
    {
        private PlayServProjectModuleSettingsDiagnostic(bool isError, string message)
        {
            IsError = isError;
            Message = message ?? string.Empty;
        }

        public bool IsError { get; }

        public string Message { get; }

        public static PlayServProjectModuleSettingsDiagnostic Error(string message)
        {
            return new PlayServProjectModuleSettingsDiagnostic(true, message);
        }

        public static PlayServProjectModuleSettingsDiagnostic Warning(string message)
        {
            return new PlayServProjectModuleSettingsDiagnostic(false, message);
        }
    }

    [InitializeOnLoad]
    internal static class PlayServProjectModuleSettingsWatcher
    {
        private const double PollIntervalSeconds = 1d;
        private static DateTime _lastWriteTimeUtc;
        private static double _nextPollTime;

        static PlayServProjectModuleSettingsWatcher()
        {
            _lastWriteTimeUtc = PlayServProjectModuleSettings.GetLastWriteTimeUtc();
            EditorApplication.update += Poll;
        }

        public static void NotifyWritten()
        {
            _lastWriteTimeUtc = PlayServProjectModuleSettings.GetLastWriteTimeUtc();
        }

        private static void Poll()
        {
            if (EditorApplication.timeSinceStartup < _nextPollTime)
                return;

            _nextPollTime = EditorApplication.timeSinceStartup + PollIntervalSeconds;
            var writeTimeUtc = PlayServProjectModuleSettings.GetLastWriteTimeUtc();
            if (writeTimeUtc == _lastWriteTimeUtc)
                return;

            _lastWriteTimeUtc = writeTimeUtc;
            PlayServModuleGraphSynchronizer.QueueSync();
        }
    }

    internal sealed class PlayServModuleBuildTargetChanged : IActiveBuildTargetChanged
    {
        public int callbackOrder => 0;

        public void OnActiveBuildTargetChanged(BuildTarget previousTarget, BuildTarget newTarget)
        {
            PlayServModuleGraphSynchronizer.QueueSync();
        }
    }
}
