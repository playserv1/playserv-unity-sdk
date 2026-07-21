using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Playserv.Modules;
using UnityEditor;
using UnityEngine;

namespace Playserv.Editor
{
    internal enum PlayServModuleValidationSeverity
    {
        Info,
        Warning,
        Error
    }

    internal sealed class PlayServModuleValidationIssue
    {
        public PlayServModuleValidationIssue(
            PlayServModuleValidationSeverity severity,
            string message,
            string moduleId = null,
            string detail = null)
        {
            Severity = severity;
            Message = message ?? string.Empty;
            ModuleId = moduleId ?? string.Empty;
            Detail = detail ?? string.Empty;
        }

        public PlayServModuleValidationSeverity Severity { get; }

        public string Message { get; }

        public string ModuleId { get; }

        public string Detail { get; }

        public string ToLogLine()
        {
            var prefix = string.IsNullOrWhiteSpace(ModuleId)
                ? "[PlayServ]"
                : $"[PlayServ:{ModuleId}]";
            return string.IsNullOrWhiteSpace(Detail)
                ? $"{prefix} {Message}"
                : $"{prefix} {Message} {Detail}";
        }
    }

    internal sealed class PlayServModuleValidationReport
    {
        private readonly PlayServModuleValidationIssue[] _issues;

        public PlayServModuleValidationReport(IEnumerable<PlayServModuleValidationIssue> issues)
        {
            _issues = (issues ?? Array.Empty<PlayServModuleValidationIssue>()).ToArray();
            for (var i = 0; i < _issues.Length; i++)
            {
                switch (_issues[i].Severity)
                {
                    case PlayServModuleValidationSeverity.Error:
                        ErrorCount++;
                        break;
                    case PlayServModuleValidationSeverity.Warning:
                        WarningCount++;
                        break;
                    default:
                        InfoCount++;
                        break;
                }
            }
        }

        public IReadOnlyList<PlayServModuleValidationIssue> Issues => _issues;

        public int ErrorCount { get; }

        public int WarningCount { get; }

        public int InfoCount { get; }

        public bool IsClean => _issues.Length == 0;

        public bool HasErrors => ErrorCount > 0;

        public string Summary
        {
            get
            {
                if (IsClean)
                    return "PlayServ module validation passed.";

                return $"PlayServ module validation found {ErrorCount} error(s), {WarningCount} warning(s), and {InfoCount} info item(s).";
            }
        }
    }

    internal static class PlayServModuleValidator
    {
        private const string ThisScriptSuffix = "/Editor/Window/PlayServModuleValidator.cs";
        private const string RuntimeAsmdefRelativePath = "Runtime/Playserv.Runtime.asmdef";
        private const string GeneratedCompatibilityRelativePath = "Runtime/Generated/Compatibility/PlayServCompatibility.g.cs";
        private const string GeneratedModuleRegistryRelativePath = "Runtime/Generated/Modules/PlayServModuleRegistry.g.cs";
        private const string GeneratedModuleManifestRelativePath = "Runtime/Modules/Contracts/Generated/PlayServGeneratedModuleManifest.g.cs";
        public static PlayServModuleValidationReport Validate()
        {
            PlayServModuleManifestJsonRegistry.Reload();
            var issues = new List<PlayServModuleValidationIssue>();
            ValidateManifestEntries(issues);
            ValidateJsonManifestMetadata(issues);
            ValidateModuleCodegenContributors(issues);
            ValidateModuleConfigSections(issues);
            ValidateScriptingDefines(issues);

            if (!TryGetPackageRoot(out var packageRoot))
            {
                issues.Add(new PlayServModuleValidationIssue(
                    PlayServModuleValidationSeverity.Error,
                    "Package root was not found. Cannot validate asset paths or asmdef references."));
                return new PlayServModuleValidationReport(issues);
            }

            var asmdefs = BuildAsmdefMap(packageRoot, issues);
            ValidateDeclaredAssetPaths(issues);
            ValidateRootAssemblyReferences(packageRoot, asmdefs, issues);
            ValidateGeneratedFiles(packageRoot, issues);

            return new PlayServModuleValidationReport(issues);
        }

        public static PlayServModuleValidationReport RunInteractive()
        {
            var report = Validate();
            LogReport(report);

            var title = report.HasErrors
                ? "PlayServ module validation failed"
                : "PlayServ module validation";
            var message = report.IsClean
                ? "No module issues found."
                : $"{report.Summary}\n\nDetails were written to the Console.";

            EditorUtility.DisplayDialog(title, message, "OK");
            return report;
        }

        public static void LogReport(PlayServModuleValidationReport report)
        {
            if (report == null)
                return;

            if (report.IsClean)
            {
                Debug.Log($"[PlayServ] {report.Summary}");
                return;
            }

            Debug.LogWarning($"[PlayServ] {report.Summary}");
            foreach (var issue in report.Issues)
            {
                switch (issue.Severity)
                {
                    case PlayServModuleValidationSeverity.Error:
                        Debug.LogError(issue.ToLogLine());
                        break;
                    case PlayServModuleValidationSeverity.Warning:
                        Debug.LogWarning(issue.ToLogLine());
                        break;
                    default:
                        Debug.Log(issue.ToLogLine());
                        break;
                }
            }
        }

        private static void ValidateManifestEntries(List<PlayServModuleValidationIssue> issues)
        {
            var modules = PlayServModuleManifest.RuntimeModules;
            if (modules == null || modules.Count == 0)
            {
                issues.Add(new PlayServModuleValidationIssue(
                    PlayServModuleValidationSeverity.Error,
                    "Module manifest is empty."));
                return;
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var labels = new HashSet<string>(StringComparer.Ordinal);
            var defines = new HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < modules.Count; i++)
            {
                var module = modules[i];
                if (module == null)
                {
                    issues.Add(new PlayServModuleValidationIssue(
                        PlayServModuleValidationSeverity.Error,
                        $"Module manifest entry #{i} is null."));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(module.Id))
                    issues.Add(new PlayServModuleValidationIssue(PlayServModuleValidationSeverity.Error, "Module id is empty."));
                else if (!ids.Add(module.Id))
                    issues.Add(new PlayServModuleValidationIssue(PlayServModuleValidationSeverity.Error, "Duplicate module id.", module.Id));

                if (string.IsNullOrWhiteSpace(module.Label))
                    issues.Add(new PlayServModuleValidationIssue(PlayServModuleValidationSeverity.Error, "Module label is empty.", module.Id));
                else if (!labels.Add(module.Label))
                    issues.Add(new PlayServModuleValidationIssue(PlayServModuleValidationSeverity.Warning, "Duplicate module label.", module.Id, module.Label));

                if (string.IsNullOrWhiteSpace(module.DisableDefine))
                    issues.Add(new PlayServModuleValidationIssue(PlayServModuleValidationSeverity.Warning, "Module disable define is empty.", module.Id));
                else if (!defines.Add(module.DisableDefine))
                    issues.Add(new PlayServModuleValidationIssue(PlayServModuleValidationSeverity.Error, "Duplicate module disable define.", module.Id, module.DisableDefine));
            }

            for (var i = 0; i < modules.Count; i++)
            {
                var module = modules[i];
                if (module == null || string.IsNullOrWhiteSpace(module.Id))
                    continue;

                ValidateDependencyIds(module, module.DependencyIds, ids, "dependency", issues);
                ValidateDependencyIds(module, module.HiddenDependencyModuleIds, ids, "hidden dependency", issues);
                ValidateDuplicateValues(module, module.DependencyIds, "dependency", issues);
                ValidateDuplicateValues(module, module.HiddenDependencyModuleIds, "hidden dependency", issues);
                ValidateDuplicateValues(module, module.AssetPaths, "asset path", issues);
                ValidateDuplicateValues(module, module.HiddenDependencyAssetPaths, "hidden asset path", issues);
                ValidateDuplicateValues(module, module.ProfileIds, "SDK profile id", issues);
                ValidateProfileIds(module, issues);
            }
        }

        private static void ValidateProfileIds(
            PlayServModuleManifestEntry module,
            List<PlayServModuleValidationIssue> issues)
        {
            for (var i = 0; i < module.ProfileIds.Length; i++)
            {
                if (PlayServSdkProfiles.TryGet(module.ProfileIds[i], out _))
                    continue;

                issues.Add(new PlayServModuleValidationIssue(
                    PlayServModuleValidationSeverity.Error,
                    "Module references an unknown SDK profile id.",
                    module.Id,
                    module.ProfileIds[i]));
            }
        }

        private static void ValidateDependencyIds(
            PlayServModuleManifestEntry module,
            string[] dependencyIds,
            ISet<string> knownIds,
            string label,
            List<PlayServModuleValidationIssue> issues)
        {
            if (dependencyIds == null)
                return;

            for (var i = 0; i < dependencyIds.Length; i++)
            {
                var dependencyId = dependencyIds[i];
                if (string.IsNullOrWhiteSpace(dependencyId))
                {
                    issues.Add(new PlayServModuleValidationIssue(
                        PlayServModuleValidationSeverity.Error,
                        $"Module has an empty {label} id.",
                        module.Id));
                    continue;
                }

                if (string.Equals(module.Id, dependencyId, StringComparison.Ordinal))
                {
                    issues.Add(new PlayServModuleValidationIssue(
                        PlayServModuleValidationSeverity.Error,
                        $"Module depends on itself as a {label}.",
                        module.Id));
                }

                if (!knownIds.Contains(dependencyId))
                {
                    issues.Add(new PlayServModuleValidationIssue(
                        PlayServModuleValidationSeverity.Error,
                        $"Module references an unknown {label} id.",
                        module.Id,
                        dependencyId));
                }
            }
        }

        private static void ValidateDuplicateValues(
            PlayServModuleManifestEntry module,
            string[] values,
            string label,
            List<PlayServModuleValidationIssue> issues)
        {
            if (values == null || values.Length < 2)
                return;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < values.Length; i++)
            {
                var value = values[i] ?? string.Empty;
                if (!seen.Add(value))
                {
                    issues.Add(new PlayServModuleValidationIssue(
                        PlayServModuleValidationSeverity.Warning,
                        $"Module has a duplicate {label}.",
                        module.Id,
                        value));
                }
            }
        }

        private static void ValidateJsonManifestMetadata(List<PlayServModuleValidationIssue> issues)
        {
            var descriptorPaths = PlayServModuleManifestJsonRegistry.DescriptorAssetPaths;
            if (descriptorPaths.Count == 0)
            {
                issues.Add(new PlayServModuleValidationIssue(
                    PlayServModuleValidationSeverity.Error,
                    $"No {PlayServModuleManifestJsonRegistry.DescriptorFileName} descriptors were found."));
            }

            foreach (var diagnostic in PlayServModuleManifestJsonRegistry.Diagnostics)
            {
                issues.Add(new PlayServModuleValidationIssue(
                    diagnostic.IsError
                        ? PlayServModuleValidationSeverity.Error
                        : PlayServModuleValidationSeverity.Warning,
                    diagnostic.Message,
                    diagnostic.ModuleId,
                    diagnostic.DescriptorAssetPath));
            }
        }

        private static void ValidateModuleCodegenContributors(List<PlayServModuleValidationIssue> issues)
        {
            IReadOnlyList<IPlayServModuleCodegenContributor> contributors;
            try
            {
                contributors = PlayServModuleCodegenRegistry.DiscoverContributors();
            }
            catch (Exception ex)
            {
                issues.Add(new PlayServModuleValidationIssue(
                    PlayServModuleValidationSeverity.Error,
                    "Failed to discover module codegen contributors.",
                    detail: ex.GetBaseException().Message));
                return;
            }

            var moduleIds = new Dictionary<string, string>(StringComparer.Ordinal);
            var orders = new Dictionary<int, string>();
            for (var i = 0; i < contributors.Count; i++)
            {
                var contributor = contributors[i];
                var typeName = contributor.GetType().FullName ?? contributor.GetType().Name;
                string moduleId;
                int order;
                try
                {
                    moduleId = contributor.ModuleId;
                    order = contributor.Order;
                }
                catch (Exception ex)
                {
                    issues.Add(new PlayServModuleValidationIssue(
                        PlayServModuleValidationSeverity.Error,
                        "Module codegen contributor metadata could not be read.",
                        detail: $"{typeName}: {ex.GetBaseException().Message}"));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(moduleId))
                {
                    issues.Add(new PlayServModuleValidationIssue(
                        PlayServModuleValidationSeverity.Error,
                        "Module codegen contributor has an empty module id.",
                        detail: typeName));
                    continue;
                }

                if (!PlayServModuleManifest.TryGet(moduleId, out var module))
                {
                    issues.Add(new PlayServModuleValidationIssue(
                        PlayServModuleValidationSeverity.Error,
                        "Module codegen contributor references an unknown module id.",
                        moduleId,
                        typeName));
                }

                if (moduleIds.TryGetValue(moduleId, out var existingType))
                {
                    issues.Add(new PlayServModuleValidationIssue(
                        PlayServModuleValidationSeverity.Error,
                        "Duplicate module codegen contributor.",
                        moduleId,
                        $"{existingType} and {typeName}"));
                }
                else
                {
                    moduleIds.Add(moduleId, typeName);
                }

                if (orders.TryGetValue(order, out var existingOrderType))
                {
                    issues.Add(new PlayServModuleValidationIssue(
                        PlayServModuleValidationSeverity.Warning,
                        "Duplicate module codegen contributor order.",
                        module?.Id ?? moduleId,
                        $"{order}: {existingOrderType} and {typeName}"));
                }
                else
                {
                    orders.Add(order, typeName);
                }
            }
        }

        private static void ValidateDeclaredAssetPaths(List<PlayServModuleValidationIssue> issues)
        {
            foreach (var module in PlayServModuleManifest.RuntimeModules)
            {
                if (!PlayServEditorModuleAvailability.TryGetModuleRoot(module, out var moduleRoot))
                {
                    issues.Add(new PlayServModuleValidationIssue(
                        PlayServModuleValidationSeverity.Error,
                        "Module package root was not found.",
                        module.Id,
                        module.SourceRootAssetPath));
                    continue;
                }

                ValidateDeclaredAssetPaths(moduleRoot, module, module.AssetPaths, "asset path", issues);
                ValidateDeclaredAssetPaths(moduleRoot, module, module.HiddenDependencyAssetPaths, "hidden asset path", issues);
            }
        }

        private static void ValidateDeclaredAssetPaths(
            PlayServPackageRoot packageRoot,
            PlayServModuleManifestEntry module,
            string[] paths,
            string label,
            List<PlayServModuleValidationIssue> issues)
        {
            if (paths == null)
                return;

            for (var i = 0; i < paths.Length; i++)
            {
                var path = NormalizeRelativePath(paths[i]);
                if (string.IsNullOrWhiteSpace(path))
                {
                    issues.Add(new PlayServModuleValidationIssue(
                        PlayServModuleValidationSeverity.Error,
                        $"Module declares an empty {label}.",
                        module.Id));
                    continue;
                }

                if (!IsSafeRelativePath(path))
                {
                    issues.Add(new PlayServModuleValidationIssue(
                        PlayServModuleValidationSeverity.Error,
                        $"Module declares an unsafe {label}.",
                        module.Id,
                        path));
                    continue;
                }

                var absolutePath = packageRoot.ToAbsolutePath(path);
                if (!IsSameOrChildPath(absolutePath, packageRoot.AbsolutePath))
                {
                    issues.Add(new PlayServModuleValidationIssue(
                        PlayServModuleValidationSeverity.Error,
                        $"Module {label} resolves outside the package root.",
                        module.Id,
                        path));
                    continue;
                }

                if (!Directory.Exists(absolutePath) && !File.Exists(absolutePath))
                {
                    issues.Add(new PlayServModuleValidationIssue(
                        PlayServModuleValidationSeverity.Warning,
                        $"Declared {label} is missing. This is expected only after intentionally uninstalling the module.",
                        module.Id,
                        path));
                }
            }
        }

        private static Dictionary<string, List<string>> BuildAsmdefMap(
            PlayServPackageRoot packageRoot,
            List<PlayServModuleValidationIssue> issues)
        {
            var asmdefs = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var roots = new Dictionary<string, PlayServPackageRoot>(StringComparer.OrdinalIgnoreCase)
            {
                [packageRoot.AbsolutePath] = packageRoot
            };

            foreach (var module in PlayServModuleManifest.RuntimeModules)
            {
                if (PlayServEditorModuleAvailability.TryGetModuleRoot(module, out var moduleRoot))
                    roots[moduleRoot.AbsolutePath] = moduleRoot;
            }

            foreach (var root in roots.Values)
            {
                string[] files;
                try
                {
                    files = Directory.GetFiles(root.AbsolutePath, "*.asmdef", SearchOption.AllDirectories);
                }
                catch (Exception ex)
                {
                    issues.Add(new PlayServModuleValidationIssue(
                        PlayServModuleValidationSeverity.Error,
                        "Failed to scan module package asmdef files.",
                        detail: $"{root.AssetPath}: {ex.Message}"));
                    continue;
                }

                for (var i = 0; i < files.Length; i++)
                {
                    var path = files[i];
                    var model = ReadAsmdef(path, issues);
                    if (model == null || string.IsNullOrWhiteSpace(model.name))
                        continue;

                    if (!asmdefs.TryGetValue(model.name, out var paths))
                    {
                        paths = new List<string>();
                        asmdefs.Add(model.name, paths);
                    }

                    paths.Add(root.ToAssetPath(path));
                }
            }

            foreach (var pair in asmdefs)
            {
                if (pair.Value.Count > 1)
                {
                    issues.Add(new PlayServModuleValidationIssue(
                        PlayServModuleValidationSeverity.Error,
                        "Duplicate asmdef name.",
                        detail: $"{pair.Key}: {string.Join(", ", pair.Value)}"));
                }
            }

            return asmdefs;
        }

        private static void ValidateRootAssemblyReferences(
            PlayServPackageRoot packageRoot,
            IReadOnlyDictionary<string, List<string>> asmdefs,
            List<PlayServModuleValidationIssue> issues)
        {
            var runtimeAsmdefPath = packageRoot.ToAbsolutePath(RuntimeAsmdefRelativePath);
            var runtimeAsmdef = ReadAsmdef(runtimeAsmdefPath, issues);
            if (runtimeAsmdef == null)
            {
                issues.Add(new PlayServModuleValidationIssue(
                    PlayServModuleValidationSeverity.Error,
                    "Runtime root asmdef is missing or invalid.",
                    detail: RuntimeAsmdefRelativePath));
                return;
            }

            var runtimeReferences = new HashSet<string>(runtimeAsmdef.references ?? Array.Empty<string>(), StringComparer.Ordinal);
            var state = PlayServRuntimeModuleDefines.LoadUserPreferenceState();
            PlayServEditorModuleAvailability.NormalizeAvailableRuntimeState(state);
            PlayServRuntimeModuleDefines.NormalizeDependencies(state);

            foreach (var module in PlayServModuleManifest.RuntimeModules)
            {
                if (string.IsNullOrWhiteSpace(module.RootAssemblyReference))
                    continue;

                var asmdefExists = asmdefs.ContainsKey(module.RootAssemblyReference);
                var moduleAvailable = PlayServEditorModuleAvailability.IsRuntimeModuleAvailable(module);
                if (moduleAvailable && !asmdefExists)
                {
                    issues.Add(new PlayServModuleValidationIssue(
                        PlayServModuleValidationSeverity.Error,
                        "Module root assembly reference does not match any package asmdef.",
                        module.Id,
                        module.RootAssemblyReference));
                }

                var shouldReference = moduleAvailable && asmdefExists && state.IsEnabled(module.Id);
                var hasReference = runtimeReferences.Contains(module.RootAssemblyReference);
                if (shouldReference && !hasReference)
                {
                    issues.Add(new PlayServModuleValidationIssue(
                        PlayServModuleValidationSeverity.Warning,
                        "Runtime root asmdef is missing an enabled module reference. Run module graph sync.",
                        module.Id,
                        module.RootAssemblyReference));
                }
                else if (!shouldReference && hasReference)
                {
                    issues.Add(new PlayServModuleValidationIssue(
                        PlayServModuleValidationSeverity.Error,
                        "Runtime root asmdef has a stale module reference. Run module graph sync.",
                        module.Id,
                        module.RootAssemblyReference));
                }
            }
        }

        private static void ValidateGeneratedFiles(PlayServPackageRoot packageRoot, List<PlayServModuleValidationIssue> issues)
        {
            ValidateGeneratedFile(packageRoot, GeneratedCompatibilityRelativePath, issues);
            ValidateGeneratedFile(packageRoot, GeneratedModuleRegistryRelativePath, issues);
            ValidateGeneratedFile(packageRoot, GeneratedModuleManifestRelativePath, issues);
        }

        private static void ValidateGeneratedFile(
            PlayServPackageRoot packageRoot,
            string relativePath,
            List<PlayServModuleValidationIssue> issues)
        {
            var absolutePath = packageRoot.ToAbsolutePath(relativePath);
            if (!File.Exists(absolutePath))
            {
                issues.Add(new PlayServModuleValidationIssue(
                    PlayServModuleValidationSeverity.Error,
                    "Generated module file is missing. Run module graph sync.",
                    detail: relativePath));
                return;
            }

            var text = File.ReadAllText(absolutePath);
            if (text.IndexOf("// <auto-generated />", StringComparison.Ordinal) < 0)
            {
                issues.Add(new PlayServModuleValidationIssue(
                    PlayServModuleValidationSeverity.Warning,
                    "Generated module file does not contain the PlayServ auto-generated marker.",
                    detail: relativePath));
            }
        }

        private static void ValidateModuleConfigSections(List<PlayServModuleValidationIssue> issues)
        {
            var moduleIds = new HashSet<string>(StringComparer.Ordinal);
            var sectionIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var section in PlayServModuleConfigSectionRegistry.RegisteredSections)
            {
                if (!PlayServModuleManifest.TryGet(section.ModuleId, out var module))
                {
                    issues.Add(new PlayServModuleValidationIssue(
                        PlayServModuleValidationSeverity.Error,
                        "Module config section references an unknown module id.",
                        section.ModuleId,
                        section.SectionId));
                    continue;
                }

                if (!sectionIds.Add(section.SectionId))
                {
                    issues.Add(new PlayServModuleValidationIssue(
                        PlayServModuleValidationSeverity.Warning,
                        "Duplicate module config section id.",
                        module.Id,
                        section.SectionId));
                }

                if (!moduleIds.Add(module.Id))
                {
                    issues.Add(new PlayServModuleValidationIssue(
                        PlayServModuleValidationSeverity.Info,
                        "Module has more than one registered config section.",
                        module.Id));
                }

                if (PlayServEditorModuleAvailability.IsRuntimeModuleAvailable(module) &&
                    !PlayServEditorSectionRegistry.IsRegistered(section.SectionId))
                {
                    issues.Add(new PlayServModuleValidationIssue(
                        PlayServModuleValidationSeverity.Error,
                        "Module config section is declared but the editor section is not registered.",
                        module.Id,
                        section.SectionId));
                }
            }
        }

        private static void ValidateScriptingDefines(List<PlayServModuleValidationIssue> issues)
        {
            var actualState = PlayServRuntimeModuleDefines.Load();
            var expectedState = PlayServRuntimeModuleDefines.LoadUserPreferenceState();
            PlayServEditorModuleAvailability.NormalizeAvailableRuntimeState(expectedState);
            PlayServRuntimeModuleDefines.NormalizeDependencies(expectedState);

            foreach (var module in PlayServModuleManifest.RuntimeModules)
            {
                var actualEnabled = actualState.IsEnabled(module.Id);
                var expectedEnabled = expectedState.IsEnabled(module.Id);
                if (actualEnabled == expectedEnabled)
                    continue;

                issues.Add(new PlayServModuleValidationIssue(
                    PlayServModuleValidationSeverity.Warning,
                    "Scripting define state does not match the saved module state. Run module repair.",
                    module.Id,
                    $"Expected {(expectedEnabled ? "enabled" : "disabled")}, found {(actualEnabled ? "enabled" : "disabled")}."));
            }
        }

        private static AsmdefModel ReadAsmdef(string absolutePath, List<PlayServModuleValidationIssue> issues)
        {
            if (!File.Exists(absolutePath))
                return null;

            try
            {
                var json = File.ReadAllText(absolutePath);
                return JsonUtility.FromJson<AsmdefModel>(json);
            }
            catch (Exception ex)
            {
                issues.Add(new PlayServModuleValidationIssue(
                    PlayServModuleValidationSeverity.Error,
                    "Failed to read asmdef.",
                    detail: $"{absolutePath}: {ex.Message}"));
                return null;
            }
        }

        private static bool TryGetPackageRoot(out PlayServPackageRoot packageRoot)
        {
            return PlayServPackagePathResolver.TryResolveRootForScript(
                nameof(PlayServModuleValidator),
                ThisScriptSuffix,
                out packageRoot);
        }

        private static string NormalizeRelativePath(string path)
        {
            return string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : path.Replace('\\', '/').Trim('/');
        }

        private static bool IsSafeRelativePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
                return false;

            var segments = path.Split('/');
            for (var i = 0; i < segments.Length; i++)
            {
                if (string.Equals(segments[i], "..", StringComparison.Ordinal))
                    return false;
            }

            return true;
        }

        private static bool IsSameOrChildPath(string fullPath, string root)
        {
            fullPath = Path.GetFullPath(fullPath);
            root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase) ||
                   fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                   fullPath.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        [Serializable]
        private sealed class AsmdefModel
        {
            public string name;
            public string[] references;
        }
    }

    internal static class PlayServModuleValidatorMenu
    {
        [MenuItem("Tools/PlayServ/Validate SDK Modules")]
        private static void ValidateSdkModules()
        {
            PlayServModuleValidator.RunInteractive();
        }
    }
}
