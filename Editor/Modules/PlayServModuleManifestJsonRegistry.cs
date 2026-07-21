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

            for (var i = 0; i < descriptorPaths.Length; i++)
            {
                var descriptorPath = descriptorPaths[i];
                if (!TryReadDescriptor(descriptorPath, diagnostics, out var model, out var sourceRootAssetPath))
                    continue;

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

            registrations.Sort(CompareRegistrations);
            PlayServModuleManifest.SetDiscoveredModules(registrations.Select(registration => registration.Module));
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
                model.profiles);
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

        private static int CompareRegistrations(ManifestRegistration left, ManifestRegistration right)
        {
            var order = left.Order.CompareTo(right.Order);
            return order != 0
                ? order
                : string.Compare(left.DescriptorAssetPath, right.DescriptorAssetPath, StringComparison.Ordinal);
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

            public void Normalize()
            {
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
