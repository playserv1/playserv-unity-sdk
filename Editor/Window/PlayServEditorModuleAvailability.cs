using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Playserv.Modules;
using UnityEditor;

namespace Playserv.Editor
{
    public static class PlayServEditorModuleAvailability
    {
        private const string ThisScriptSuffix = "/Editor/Window/PlayServEditorModuleAvailability.cs";
        private static PlayServPackageRoot _packageRoot;

        internal static IEnumerable<PlayServRuntimeModuleDefinition> AvailableRuntimeModules =>
            BuildRuntimeModuleDefinitions().Where(module => IsRuntimeModuleAvailable(module.Id));

        public static IEnumerable<PlayServModuleManifestEntry> AvailableExportModules =>
            PlayServModuleManifest.ExportableRuntimeModules.Where(IsRuntimeModuleAvailable);

        public static bool EditorDeployment => HasFolder("Editor/Deploy");
        public static bool EditorModelSync => HasFolder("Editor/ModelGenerator");
        public static bool EditorCodegen => HasFolder("Editor/CodeGenerator") && HasFolder("Runtime/CodeGenerator/Shared");
        public static bool EditorEvents =>
            HasFolder("Editor/Events") &&
            IsRuntimeModuleAvailable(PlayServModuleManifest.EventsId);
        public static bool EditorModuleStressTests => HasFolder("Editor/ModuleStressTests");

        public static bool HasAnyEditorTool =>
            EditorDeployment ||
            EditorModelSync ||
            EditorCodegen ||
            EditorModuleStressTests;

        private static PlayServRuntimeModuleDefinition[] BuildRuntimeModuleDefinitions()
        {
            return PlayServModuleManifest.VisibleRuntimeModules
                .Select(CreateRuntimeModuleDefinition)
                .ToArray();
        }

        private static PlayServRuntimeModuleDefinition CreateRuntimeModuleDefinition(PlayServModuleManifestEntry module)
        {
            return new PlayServRuntimeModuleDefinition(
                module.Id,
                module.Label,
                module.Description,
                ToLabels(module.DependencyIds),
                BuildDependentLabels(module.Id),
                settings => settings.IsRuntimeModuleEnabled(module.Id),
                (settings, enabled) => settings.SetRuntimeModuleEnabled(module.Id, enabled));
        }

        private static string[] ToLabels(string[] moduleIds)
        {
            if (moduleIds == null || moduleIds.Length == 0)
                return Array.Empty<string>();

            return moduleIds
                .Select(PlayServModuleManifest.GetLabel)
                .ToArray();
        }

        private static string[] BuildDependentLabels(string moduleId)
        {
            return PlayServModuleManifest.VisibleRuntimeModules
                .Where(module => module.DependencyIds.Contains(moduleId, StringComparer.Ordinal))
                .Select(module => module.Label)
                .ToArray();
        }

        public static bool IsRuntimeModuleAvailable(string moduleIdOrLabel)
        {
            return PlayServModuleManifest.TryResolve(moduleIdOrLabel, out var module) &&
                   IsRuntimeModuleAvailable(module, new HashSet<string>(StringComparer.Ordinal));
        }

        public static bool IsRuntimeModuleAvailable(PlayServModuleManifestEntry module)
        {
            return module != null &&
                   IsRuntimeModuleAvailable(module, new HashSet<string>(StringComparer.Ordinal));
        }

        private static bool IsRuntimeModuleAvailable(
            PlayServModuleManifestEntry module,
            ISet<string> visited)
        {
            if (module == null)
                return false;

            if (!visited.Add(module.Id))
                return true;

            if (!HasRequiredAssets(module, module.AssetPaths) ||
                !HasRequiredAssets(module, module.HiddenDependencyAssetPaths))
            {
                return false;
            }

            if (!AreDependenciesAvailable(module.DependencyIds, visited) ||
                !AreDependenciesAvailable(module.HiddenDependencyModuleIds, visited))
            {
                return false;
            }

            return true;
        }

        private static bool AreDependenciesAvailable(string[] dependencyIds, ISet<string> visited)
        {
            for (var i = 0; i < dependencyIds.Length; i++)
            {
                if (!PlayServModuleManifest.TryGet(dependencyIds[i], out var dependency) ||
                    !IsRuntimeModuleAvailable(dependency, visited))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasRequiredAssets(PlayServModuleManifestEntry module, string[] relativePaths)
        {
            for (var i = 0; i < relativePaths.Length; i++)
            {
                if (!HasAssetPath(module, relativePaths[i]))
                    return false;
            }

            return true;
        }

        internal static void NormalizeAvailableRuntimeState(PlayServRuntimeModuleState state)
        {
            if (state == null)
                return;

            foreach (var module in PlayServModuleManifest.RuntimeModules)
            {
                if (!IsRuntimeModuleAvailable(module))
                    state.SetEnabled(module.Id, false);
            }

            PlayServRuntimeModuleDefines.NormalizeDependencies(state);
        }

        public static void SyncUnavailableModuleDefines()
        {
            PlayServRuntimeModuleDefines.RemoveStaleDefaultDisableDefines(GetAvailableDefaultEnabledModuleIds());

            var defines = ReadDefines();
            var changed = PlayServRuntimeModuleDefines.RemoveLegacyRuntimeModuleDefines(defines);

            changed |= SetDisabled(defines, Const.DefineDisableEditorDeployment, !EditorDeployment);
            changed |= SetDisabled(defines, Const.DefineDisableEditorModelSync, !EditorModelSync);
            changed |= SetDisabled(defines, Const.DefineDisableEditorCodegen, !EditorCodegen);

            if (changed)
                WriteDefines(defines);
        }

        private static IEnumerable<string> GetAvailableDefaultEnabledModuleIds()
        {
            foreach (var module in PlayServModuleManifest.RuntimeModules)
            {
                if (!module.DefaultEnabled || !IsRuntimeModuleAvailable(module))
                    continue;

                yield return module.Id;
            }
        }

        private static bool HasAssetPath(PlayServModuleManifestEntry module, string relativePath)
        {
            if (!TryGetModuleRoot(module, out var moduleRoot))
                return false;

            var absolutePath = moduleRoot.ToAbsolutePath(relativePath);
            return Directory.Exists(absolutePath) || File.Exists(absolutePath);
        }

        internal static bool TryGetModuleRoot(PlayServModuleManifestEntry module, out PlayServPackageRoot moduleRoot)
        {
            moduleRoot = null;
            if (module != null && !string.IsNullOrWhiteSpace(module.SourceRootAssetPath))
            {
                moduleRoot = new PlayServPackageRoot(
                    module.SourceRootAssetPath,
                    PlayServPackagePathResolver.ToAbsoluteAssetPath(module.SourceRootAssetPath));
                return moduleRoot.Exists;
            }

            return TryGetPackageRoot(out moduleRoot);
        }

        private static bool HasFolder(string relativePath)
        {
            return TryGetPackageRoot(out var packageRoot) &&
                   Directory.Exists(packageRoot.ToAbsolutePath(relativePath));
        }

        private static bool TryGetPackageRoot(out PlayServPackageRoot packageRoot)
        {
            if (_packageRoot != null)
            {
                if (_packageRoot.Exists)
                {
                    packageRoot = _packageRoot;
                    return true;
                }

                _packageRoot = null;
            }

            if (!PlayServPackagePathResolver.TryResolveRootForScript(
                nameof(PlayServEditorModuleAvailability),
                ThisScriptSuffix,
                out packageRoot))
            {
                return false;
            }

            _packageRoot = packageRoot;
            return true;
        }

        private static bool SetDisabled(ISet<string> defines, string symbol, bool disabled)
        {
            return disabled ? defines.Add(symbol) : defines.Remove(symbol);
        }

        private static ISet<string> ReadDefines()
        {
            var group = EditorUserBuildSettings.selectedBuildTargetGroup;
            var rawDefines = PlayerSettings.GetScriptingDefineSymbolsForGroup(group);
            var symbols = rawDefines
                .Split(';')
                .Select(symbol => symbol.Trim())
                .Where(symbol => !string.IsNullOrEmpty(symbol));

            return new HashSet<string>(symbols, System.StringComparer.Ordinal);
        }

        private static void WriteDefines(ISet<string> defines)
        {
            var group = EditorUserBuildSettings.selectedBuildTargetGroup;
            var value = string.Join(";", defines.OrderBy(symbol => symbol, System.StringComparer.Ordinal));
            PlayerSettings.SetScriptingDefineSymbolsForGroup(group, value);
        }

        internal sealed class PlayServRuntimeModuleDefinition
        {
            private readonly Func<PlayServEditorModuleSettings, bool> _isEnabled;
            private readonly Func<PlayServEditorModuleSettings, bool, bool> _setEnabled;

            public PlayServRuntimeModuleDefinition(
                string id,
                string name,
                string description,
                string[] dependencies,
                string[] dependents,
                Func<PlayServEditorModuleSettings, bool> isEnabled,
                Func<PlayServEditorModuleSettings, bool, bool> setEnabled)
            {
                Id = id ?? throw new ArgumentNullException(nameof(id));
                Name = name ?? throw new ArgumentNullException(nameof(name));
                Description = description ?? string.Empty;
                Dependencies = dependencies ?? Array.Empty<string>();
                Dependents = dependents ?? Array.Empty<string>();
                _isEnabled = isEnabled ?? throw new ArgumentNullException(nameof(isEnabled));
                _setEnabled = setEnabled ?? throw new ArgumentNullException(nameof(setEnabled));
            }

            public string Id { get; }
            public string Name { get; }
            public string Description { get; }
            public string[] Dependencies { get; }
            public string[] Dependents { get; }

            public bool IsEnabled(PlayServEditorModuleSettings settings)
            {
                return settings != null && _isEnabled(settings);
            }

            public bool SetEnabled(PlayServEditorModuleSettings settings, bool enabled)
            {
                return settings != null && _setEnabled(settings, enabled);
            }
        }
    }
}
