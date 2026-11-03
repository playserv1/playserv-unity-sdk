using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Playserv.Modules;
using UnityEditor;
using UnityEngine;
using PackageManagerPackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Playserv.Editor
{
    [InitializeOnLoad]
    internal static class PlayServModuleManifestJsonRegistry
    {
        public const string DescriptorFileName = "module.playserv.json";
        public const int CurrentSchemaVersion = 1;

        private static readonly HashSet<string> KnownPlatforms = new HashSet<string>(
            new[]
            {
                "Editor",
                "Standalone",
                "Android",
                "iOS",
                "WebGL"
            },
            StringComparer.Ordinal);

        private static PlayServModuleManifestJsonDiagnostic[] _diagnostics =
            Array.Empty<PlayServModuleManifestJsonDiagnostic>();
        private static string[] _descriptorAssetPaths = Array.Empty<string>();

        static PlayServModuleManifestJsonRegistry()
        {
            Reload();
        }

        public static IReadOnlyList<PlayServModuleManifestJsonDiagnostic> Diagnostics => _diagnostics;

        public static IReadOnlyList<string> DescriptorAssetPaths => _descriptorAssetPaths;

        public static void Reload()
        {
            var diagnostics = new List<PlayServModuleManifestJsonDiagnostic>();
            var registrations = new List<ManifestRegistration>();
            var descriptorPaths = FindDescriptorAssetPaths();
            var moduleIds = new Dictionary<string, string>(StringComparer.Ordinal);
            var orders = new Dictionary<int, string>();
            PlayServPackageVersionProvider.TryResolveInstalledVersion(out var installedSdkVersion);

            for (var i = 0; i < descriptorPaths.Length; i++)
            {
                var descriptorPath = descriptorPaths[i];
                if (!TryReadDescriptor(
                        descriptorPath,
                        installedSdkVersion,
                        diagnostics,
                        out var model,
                        out var sourceRootAssetPath))
                {
                    continue;
                }

                if (moduleIds.TryGetValue(model.id, out var existingPath))
                {
                    diagnostics.Add(PlayServModuleManifestJsonDiagnostic.Error(
                        descriptorPath,
                        model.id,
                        $"Duplicate module id. Already declared by {existingPath}."));
                    continue;
                }

                moduleIds.Add(model.id, descriptorPath);
                if (orders.TryGetValue(model.order, out var existingOrderPath))
                {
                    diagnostics.Add(PlayServModuleManifestJsonDiagnostic.Warning(
                        descriptorPath,
                        model.id,
                        $"Duplicate module order {model.order}. Also used by {existingOrderPath}."));
                }
                else
                {
                    orders.Add(model.order, descriptorPath);
                }

                registrations.Add(new ManifestRegistration(
                    model.order,
                    descriptorPath,
                    CreateEntry(model, sourceRootAssetPath, descriptorPath)));
            }

            ValidateCrossDescriptorMetadata(registrations, diagnostics);
            var graph = PlayServModuleDependencyGraph.Order(
                registrations.Select(registration => registration.Module));
            for (var i = 0; i < graph.CyclePaths.Length; i++)
            {
                var cyclePath = graph.CyclePaths[i];
                var moduleId = cyclePath.Split(new[] { " -> " }, StringSplitOptions.None)[0];
                var descriptorPath = registrations
                    .Where(registration => string.Equals(
                        registration.Module.Id,
                        moduleId,
                        StringComparison.Ordinal))
                    .Select(registration => registration.DescriptorAssetPath)
                    .FirstOrDefault() ?? string.Empty;
                diagnostics.Add(PlayServModuleManifestJsonDiagnostic.Error(
                    descriptorPath,
                    moduleId,
                    $"Cyclic module dependency detected: {cyclePath}."));
            }

            PlayServModuleManifest.SetDiscoveredModules(graph.OrderedModules);
            _descriptorAssetPaths = descriptorPaths;
            _diagnostics = diagnostics.ToArray();
        }

        private static string[] FindDescriptorAssetPaths()
        {
            return AssetDatabase.FindAssets("module.playserv")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(NormalizeAssetPath)
                .Where(path => path.EndsWith("/" + DescriptorFileName, StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(path, DescriptorFileName, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }

        private static bool TryReadDescriptor(
            string descriptorAssetPath,
            string installedSdkVersion,
            ICollection<PlayServModuleManifestJsonDiagnostic> diagnostics,
            out ManifestJson model,
            out string sourceRootAssetPath)
        {
            model = null;
            sourceRootAssetPath = string.Empty;

            try
            {
                var absolutePath = PlayServPackagePathResolver.ToAbsoluteAssetPath(descriptorAssetPath);
                if (!File.Exists(absolutePath))
                {
                    diagnostics.Add(PlayServModuleManifestJsonDiagnostic.Error(
                        descriptorAssetPath,
                        string.Empty,
                        "Descriptor file does not exist."));
                    return false;
                }

                model = JsonUtility.FromJson<ManifestJson>(File.ReadAllText(absolutePath));
                if (model == null)
                {
                    diagnostics.Add(PlayServModuleManifestJsonDiagnostic.Error(
                        descriptorAssetPath,
                        string.Empty,
                        "Descriptor JSON is empty or invalid."));
                    return false;
                }

                model.Normalize();
                if (string.IsNullOrWhiteSpace(model.id))
                {
                    diagnostics.Add(PlayServModuleManifestJsonDiagnostic.Error(
                        descriptorAssetPath,
                        string.Empty,
                        "Module id is required."));
                    return false;
                }

                if (string.IsNullOrWhiteSpace(model.disableDefine))
                {
                    diagnostics.Add(PlayServModuleManifestJsonDiagnostic.Error(
                        descriptorAssetPath,
                        model.id,
                        "disableDefine is required."));
                    return false;
                }

                if (model.schemaVersion > CurrentSchemaVersion)
                {
                    diagnostics.Add(PlayServModuleManifestJsonDiagnostic.Error(
                        descriptorAssetPath,
                        model.id,
                        $"Unsupported descriptor schemaVersion {model.schemaVersion}. " +
                        $"This SDK supports up to {CurrentSchemaVersion}."));
                    return false;
                }

                if (!ValidateMinimumSdkVersion(
                        descriptorAssetPath,
                        model,
                        installedSdkVersion,
                        diagnostics))
                {
                    return false;
                }

                for (var i = 0; i < model.supportedPlatforms.Length; i++)
                {
                    if (KnownPlatforms.Contains(model.supportedPlatforms[i]))
                        continue;

                    diagnostics.Add(PlayServModuleManifestJsonDiagnostic.Error(
                        descriptorAssetPath,
                        model.id,
                        $"Unknown supported platform '{model.supportedPlatforms[i]}'."));
                    return false;
                }

                sourceRootAssetPath = ResolveSourceRootAssetPath(descriptorAssetPath);
                return true;
            }
            catch (Exception ex)
            {
                diagnostics.Add(PlayServModuleManifestJsonDiagnostic.Error(
                    descriptorAssetPath,
                    model?.id ?? string.Empty,
                    ex.GetBaseException().Message));
                return false;
            }
        }

        private static PlayServModuleManifestEntry CreateEntry(
            ManifestJson model,
            string sourceRootAssetPath,
            string descriptorAssetPath)
        {
            return new PlayServModuleManifestEntry(
                model.id,
                model.label,
                model.description,
                model.disableDefine,
                model.defaultEnabled,
                model.isServerModule,
                model.visibleInSettings,
                model.visibleInExport,
                model.assetPaths,
                model.dependencyIds,
                model.hiddenDependencyAssetPaths,
                model.hiddenDependencyModuleIds,
                model.rootAssemblyReference,
                model.order,
                sourceRootAssetPath,
                descriptorAssetPath,
                model.profiles,
                model.schemaVersion,
                model.minSdkVersion,
                model.supportedPlatforms,
                model.conflictsWith,
                model.capabilities,
                model.requiresPackages);
        }

        private static void ValidateCrossDescriptorMetadata(
            IReadOnlyCollection<ManifestRegistration> registrations,
            ICollection<PlayServModuleManifestJsonDiagnostic> diagnostics)
        {
            var moduleIds = new HashSet<string>(
                registrations.Select(registration => registration.Module.Id),
                StringComparer.Ordinal);

            foreach (var registration in registrations)
            {
                var module = registration.Module;
                for (var i = 0; i < module.ConflictsWith.Length; i++)
                {
                    var conflictId = module.ConflictsWith[i];
                    if (string.Equals(module.Id, conflictId, StringComparison.Ordinal))
                    {
                        diagnostics.Add(PlayServModuleManifestJsonDiagnostic.Error(
                            registration.DescriptorAssetPath,
                            module.Id,
                            "Module cannot conflict with itself."));
                    }
                    else if (!moduleIds.Contains(conflictId))
                    {
                        diagnostics.Add(PlayServModuleManifestJsonDiagnostic.Error(
                            registration.DescriptorAssetPath,
                            module.Id,
                            $"Module conflicts with unknown module id '{conflictId}'."));
                    }
                }
            }
        }

        private static bool ValidateMinimumSdkVersion(
            string descriptorAssetPath,
            ManifestJson model,
            string installedSdkVersion,
            ICollection<PlayServModuleManifestJsonDiagnostic> diagnostics)
        {
            if (string.IsNullOrWhiteSpace(model.minSdkVersion))
                return true;

            if (!SemanticVersion.TryParse(model.minSdkVersion, out var minimumVersion))
            {
                diagnostics.Add(PlayServModuleManifestJsonDiagnostic.Error(
                    descriptorAssetPath,
                    model.id,
                    $"Invalid minSdkVersion '{model.minSdkVersion}'."));
                return false;
            }

            if (string.IsNullOrWhiteSpace(installedSdkVersion))
            {
                diagnostics.Add(PlayServModuleManifestJsonDiagnostic.Warning(
                    descriptorAssetPath,
                    model.id,
                    $"Could not resolve the installed SDK version required to check minSdkVersion {model.minSdkVersion}."));
                return true;
            }

            if (!SemanticVersion.TryParse(installedSdkVersion, out var currentVersion))
            {
                diagnostics.Add(PlayServModuleManifestJsonDiagnostic.Warning(
                    descriptorAssetPath,
                    model.id,
                    $"Installed SDK version '{installedSdkVersion}' is not valid semantic versioning."));
                return true;
            }

            if (currentVersion.CompareTo(minimumVersion) >= 0)
                return true;

            diagnostics.Add(PlayServModuleManifestJsonDiagnostic.Error(
                descriptorAssetPath,
                model.id,
                $"Module requires PlayServ SDK {model.minSdkVersion} or newer; installed version is {installedSdkVersion}."));
            return false;
        }

        private static string ResolveSourceRootAssetPath(string descriptorAssetPath)
        {
            var packageInfo = PackageManagerPackageInfo.FindForAssetPath(descriptorAssetPath);
            if (packageInfo != null && !string.IsNullOrWhiteSpace(packageInfo.assetPath))
                return NormalizeAssetPath(packageInfo.assetPath);

            var directory = NormalizeAssetPath(Path.GetDirectoryName(descriptorAssetPath));
            while (!string.IsNullOrEmpty(directory))
            {
                var packageJsonAssetPath = directory + "/package.json";
                var packageJsonAbsolutePath = PlayServPackagePathResolver.ToAbsoluteAssetPath(packageJsonAssetPath);
                if (File.Exists(packageJsonAbsolutePath))
                    return directory;

                var parent = NormalizeAssetPath(Path.GetDirectoryName(directory));
                if (string.Equals(parent, directory, StringComparison.OrdinalIgnoreCase))
                    break;

                directory = parent;
            }

            return NormalizeAssetPath(Path.GetDirectoryName(descriptorAssetPath));
        }

        private static string NormalizeAssetPath(string path)
        {
            return string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : path.Replace('\\', '/').Trim('/');
        }

        [Serializable]
        private sealed class ManifestJson
        {
            public int schemaVersion;
            public string id;
            public int order;
            public string label;
            public string description;
            public string disableDefine;
            public bool defaultEnabled;
            public bool isServerModule;
            public bool visibleInSettings;
            public bool visibleInExport;
            public string[] assetPaths;
            public string[] dependencyIds;
            public string[] hiddenDependencyAssetPaths;
            public string[] hiddenDependencyModuleIds;
            public string rootAssemblyReference;
            public string[] profiles;
            public string minSdkVersion;
            public string[] supportedPlatforms;
            public string[] conflictsWith;
            public string[] capabilities;
            public string[] requiresPackages;

            public void Normalize()
            {
                if (schemaVersion <= 0)
                    schemaVersion = CurrentSchemaVersion;

                id = (id ?? string.Empty).Trim();
                label = string.IsNullOrWhiteSpace(label) ? id : label.Trim();
                description = description ?? string.Empty;
                disableDefine = (disableDefine ?? string.Empty).Trim();
                rootAssemblyReference = (rootAssemblyReference ?? string.Empty).Trim();
                assetPaths = NormalizePaths(assetPaths);
                dependencyIds = NormalizeValues(dependencyIds);
                hiddenDependencyAssetPaths = NormalizePaths(hiddenDependencyAssetPaths);
                hiddenDependencyModuleIds = NormalizeValues(hiddenDependencyModuleIds);
                profiles = NormalizeValues(profiles);
                minSdkVersion = (minSdkVersion ?? string.Empty).Trim();
                supportedPlatforms = NormalizeValues(supportedPlatforms);
                conflictsWith = NormalizeValues(conflictsWith);
                capabilities = NormalizeValues(capabilities);
                requiresPackages = NormalizeValues(requiresPackages);
            }

            private static string[] NormalizePaths(string[] values)
            {
                return NormalizeValues(values)
                    .Select(value => value.Replace('\\', '/').Trim('/'))
                    .ToArray();
            }

            private static string[] NormalizeValues(string[] values)
            {
                return (values ?? Array.Empty<string>())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.Trim())
                    .ToArray();
            }
        }

        private sealed class ManifestRegistration
        {
            public ManifestRegistration(int order, string descriptorAssetPath, PlayServModuleManifestEntry module)
            {
                Order = order;
                DescriptorAssetPath = descriptorAssetPath;
                Module = module;
            }

            public int Order { get; }

            public string DescriptorAssetPath { get; }

            public PlayServModuleManifestEntry Module { get; }
        }

        private sealed class SemanticVersion : IComparable<SemanticVersion>
        {
            private SemanticVersion(int major, int minor, int patch, string[] prerelease)
            {
                Major = major;
                Minor = minor;
                Patch = patch;
                Prerelease = prerelease;
            }

            private int Major { get; }

            private int Minor { get; }

            private int Patch { get; }

            private string[] Prerelease { get; }

            public static bool TryParse(string value, out SemanticVersion version)
            {
                version = null;
                if (string.IsNullOrWhiteSpace(value))
                    return false;

                var normalized = value.Trim();
                var buildIndex = normalized.IndexOf('+');
                if (buildIndex >= 0)
                    normalized = normalized.Substring(0, buildIndex);

                var prerelease = Array.Empty<string>();
                var prereleaseIndex = normalized.IndexOf('-');
                if (prereleaseIndex >= 0)
                {
                    prerelease = normalized.Substring(prereleaseIndex + 1)
                        .Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
                    normalized = normalized.Substring(0, prereleaseIndex);
                    if (prerelease.Length == 0)
                        return false;
                }

                var parts = normalized.Split('.');
                if (parts.Length < 1 || parts.Length > 3 ||
                    !TryParsePart(parts, 0, out var major) ||
                    !TryParsePart(parts, 1, out var minor) ||
                    !TryParsePart(parts, 2, out var patch))
                {
                    return false;
                }

                version = new SemanticVersion(major, minor, patch, prerelease);
                return true;
            }

            public int CompareTo(SemanticVersion other)
            {
                if (other == null)
                    return 1;

                var comparison = Major.CompareTo(other.Major);
                if (comparison != 0)
                    return comparison;

                comparison = Minor.CompareTo(other.Minor);
                if (comparison != 0)
                    return comparison;

                comparison = Patch.CompareTo(other.Patch);
                if (comparison != 0)
                    return comparison;

                if (Prerelease.Length == 0)
                    return other.Prerelease.Length == 0 ? 0 : 1;
                if (other.Prerelease.Length == 0)
                    return -1;

                var count = Math.Max(Prerelease.Length, other.Prerelease.Length);
                for (var i = 0; i < count; i++)
                {
                    if (i >= Prerelease.Length)
                        return -1;
                    if (i >= other.Prerelease.Length)
                        return 1;

                    comparison = ComparePrereleasePart(Prerelease[i], other.Prerelease[i]);
                    if (comparison != 0)
                        return comparison;
                }

                return 0;
            }

            private static bool TryParsePart(string[] parts, int index, out int value)
            {
                value = 0;
                return index >= parts.Length ||
                       (int.TryParse(parts[index], out value) && value >= 0);
            }

            private static int ComparePrereleasePart(string left, string right)
            {
                var leftNumeric = int.TryParse(left, out var leftNumber);
                var rightNumeric = int.TryParse(right, out var rightNumber);
                if (leftNumeric && rightNumeric)
                    return leftNumber.CompareTo(rightNumber);
                if (leftNumeric)
                    return -1;
                if (rightNumeric)
                    return 1;

                return string.Compare(left, right, StringComparison.Ordinal);
            }
        }
    }

    internal sealed class PlayServModuleManifestJsonDiagnostic
    {
        private PlayServModuleManifestJsonDiagnostic(bool isError, string descriptorAssetPath, string moduleId, string message)
        {
            IsError = isError;
            DescriptorAssetPath = descriptorAssetPath ?? string.Empty;
            ModuleId = moduleId ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public bool IsError { get; }

        public string DescriptorAssetPath { get; }

        public string ModuleId { get; }

        public string Message { get; }

        public static PlayServModuleManifestJsonDiagnostic Error(string path, string moduleId, string message)
        {
            return new PlayServModuleManifestJsonDiagnostic(true, path, moduleId, message);
        }

        public static PlayServModuleManifestJsonDiagnostic Warning(string path, string moduleId, string message)
        {
            return new PlayServModuleManifestJsonDiagnostic(false, path, moduleId, message);
        }
    }

    internal sealed class PlayServModuleManifestJsonPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (!TouchesDescriptor(importedAssets) &&
                !TouchesDescriptor(deletedAssets) &&
                !TouchesDescriptor(movedAssets) &&
                !TouchesDescriptor(movedFromAssetPaths))
            {
                return;
            }

            PlayServModuleManifestJsonRegistry.Reload();
            PlayServModuleGraphSynchronizer.QueueSync();
        }

        private static bool TouchesDescriptor(string[] assetPaths)
        {
            if (assetPaths == null)
                return false;

            for (var i = 0; i < assetPaths.Length; i++)
            {
                var path = (assetPaths[i] ?? string.Empty).Replace('\\', '/');
                if (path.EndsWith("/" + PlayServModuleManifestJsonRegistry.DescriptorFileName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
