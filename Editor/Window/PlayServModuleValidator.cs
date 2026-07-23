using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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

        public static PlayServModuleValidationReport Validate()
        {
            PlayServModuleManifestJsonRegistry.Reload();
            var issues = new List<PlayServModuleValidationIssue>();
            ValidateManifestEntries(issues);
            ValidateJsonManifestMetadata(issues);
            ValidateProjectModuleSettings(issues);
            ValidateAssemblyModuleRegistrations(issues);
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
            ValidateProjectGeneratedFiles(issues);

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

        public static void RunFromCli()
        {
            var report = Validate();
            LogReport(report);
            EditorApplication.Exit(report.HasErrors ? 1 : 0);
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

                if (module.SchemaVersion != PlayServModuleManifestJsonRegistry.CurrentSchemaVersion)
                {
                    issues.Add(new PlayServModuleValidationIssue(
                        PlayServModuleValidationSeverity.Error,
                        "Module descriptor schema version does not match the supported version.",
                        module.Id,
                        module.SchemaVersion.ToString()));
                }

                if (string.IsNullOrWhiteSpace(module.MinSdkVersion))
                {
                    issues.Add(new PlayServModuleValidationIssue(
                        PlayServModuleValidationSeverity.Warning,
                        "Module descriptor does not declare minSdkVersion.",
                        module.Id));
                }

                if (module.SupportedPlatforms.Length == 0)
                {
                    issues.Add(new PlayServModuleValidationIssue(
                        PlayServModuleValidationSeverity.Warning,
                        "Module descriptor does not declare supportedPlatforms.",
                        module.Id));
                }
            }

            for (var i = 0; i < modules.Count; i++)
            {
                var module = modules[i];
                if (module == null || string.IsNullOrWhiteSpace(module.Id))
                    continue;

                ValidateDependencyIds(module, module.DependencyIds, ids, "dependency", issues);
                ValidateDependencyIds(module, module.HiddenDependencyModuleIds, ids, "hidden dependency", issues);
                ValidateDependencyIds(module, module.ConflictsWith, ids, "conflict", issues);
                ValidateDuplicateValues(module, module.DependencyIds, "dependency", issues);
                ValidateDuplicateValues(module, module.HiddenDependencyModuleIds, "hidden dependency", issues);
                ValidateDuplicateValues(module, module.AssetPaths, "asset path", issues);
                ValidateDuplicateValues(module, module.HiddenDependencyAssetPaths, "hidden asset path", issues);
                ValidateDuplicateValues(module, module.ProfileIds, "SDK profile id", issues);
                ValidateDuplicateValues(module, module.SupportedPlatforms, "supported platform", issues);
                ValidateDuplicateValues(module, module.ConflictsWith, "conflict", issues);
                ValidateDuplicateValues(module, module.Capabilities, "capability", issues);
                ValidateDuplicateValues(module, module.RequiresPackages, "required package", issues);
                ValidateProfileIds(module, issues);
            }

            ValidateTopologicalOrder(modules, issues);
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
                        $"Module references itself as a {label}.",
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

        private static void ValidateTopologicalOrder(
            IReadOnlyList<PlayServModuleManifestEntry> modules,
            List<PlayServModuleValidationIssue> issues)
        {
            var indexes = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < modules.Count; i++)
                indexes[modules[i].Id] = i;

            for (var i = 0; i < modules.Count; i++)
            {
                var module = modules[i];
                var dependencyIds = module.DependencyIds
                    .Concat(module.HiddenDependencyModuleIds)
                    .Distinct(StringComparer.Ordinal);
                foreach (var dependencyId in dependencyIds)
                {
                    if (!indexes.TryGetValue(dependencyId, out var dependencyIndex) ||
                        dependencyIndex < i)
                    {
                        continue;
                    }

                    issues.Add(new PlayServModuleValidationIssue(
                        PlayServModuleValidationSeverity.Error,
                        "Module graph is not in topological order.",
                        module.Id,
                        $"{dependencyId} must appear before {module.Id}."));
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

        private static void ValidateProjectModuleSettings(List<PlayServModuleValidationIssue> issues)
        {
            foreach (var diagnostic in PlayServProjectModuleSettings.Validate())
            {
                issues.Add(new PlayServModuleValidationIssue(
                    diagnostic.IsError
                        ? PlayServModuleValidationSeverity.Error
                        : PlayServModuleValidationSeverity.Warning,
                    diagnostic.Message,
                    detail: PlayServProjectModuleSettings.ProjectRelativePath));
            }
        }

        private static void ValidateAssemblyModuleRegistrations(List<PlayServModuleValidationIssue> issues)
        {
            var moduleRegistrations = new Dictionary<string, Type>(StringComparer.Ordinal);
            var loadedAssemblies = new Dictionary<string, Assembly>(StringComparer.Ordinal);

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                loadedAssemblies[assembly.GetName().Name] = assembly;

                PlayServModuleAttribute[] moduleAttributes;
                try
                {
                    moduleAttributes = assembly
                        .GetCustomAttributes(typeof(PlayServModuleAttribute), inherit: false)
                        .OfType<PlayServModuleAttribute>()
                        .ToArray();
                }
                catch (Exception ex)
                {
                    issues.Add(new PlayServModuleValidationIssue(
                        PlayServModuleValidationSeverity.Warning,
                        "Could not inspect assembly module registrations.",
                        detail: $"{assembly.GetName().Name}: {ex.Message}"));
                    continue;
                }

                for (var i = 0; i < moduleAttributes.Length; i++)
                {
                    var registration = moduleAttributes[i];
                    if (!PlayServModuleManifest.TryGet(registration.ModuleId, out var module))
                    {
                        issues.Add(new PlayServModuleValidationIssue(
                            PlayServModuleValidationSeverity.Error,
                            "Assembly registration references an unknown module id.",
                            registration.ModuleId,
                            assembly.GetName().Name));
                        continue;
                    }

                    if (!typeof(IPlayServModule).IsAssignableFrom(registration.ModuleType))
                    {
                        issues.Add(new PlayServModuleValidationIssue(
                            PlayServModuleValidationSeverity.Error,
                            "Assembly registration type does not implement IPlayServModule.",
                            registration.ModuleId,
                            registration.ModuleType.FullName));
                    }

                    if (module.Order != registration.Order)
                    {
                        issues.Add(new PlayServModuleValidationIssue(
                            PlayServModuleValidationSeverity.Warning,
                            "Assembly registration order differs from the module descriptor.",
                            registration.ModuleId,
                            $"{registration.Order} != {module.Order}"));
                    }

                    if (moduleRegistrations.TryGetValue(registration.ModuleId, out var registeredType) &&
                        registeredType != registration.ModuleType)
                    {
                        issues.Add(new PlayServModuleValidationIssue(
                            PlayServModuleValidationSeverity.Error,
                            "Multiple runtime module types register the same module id.",
                            registration.ModuleId,
                            $"{registeredType.FullName}, {registration.ModuleType.FullName}"));
                    }
                    else
                    {
                        moduleRegistrations[registration.ModuleId] = registration.ModuleType;
                    }

                    if (!string.IsNullOrWhiteSpace(module.RootAssemblyReference) &&
                        !string.Equals(
                            registration.ModuleType.Assembly.GetName().Name,
                            module.RootAssemblyReference,
                            StringComparison.Ordinal))
                    {
                        issues.Add(new PlayServModuleValidationIssue(
                            PlayServModuleValidationSeverity.Error,
                            "Assembly registration is declared by a different assembly than the module descriptor.",
                            registration.ModuleId,
                            $"{registration.ModuleType.Assembly.GetName().Name} != {module.RootAssemblyReference}"));
                    }
                }

            }

            var activeState = PlayServRuntimeModuleDefines.Load();
            foreach (var module in PlayServModuleManifest.RuntimeModules)
            {
                if (!activeState.IsEnabled(module.Id) ||
                    !PlayServEditorModuleAvailability.IsRuntimeModuleAvailable(module))
                {
                    continue;
                }

                if (IsTransportModule(module))
                {
                    ValidateTransportModuleBootstrap(module, loadedAssemblies, issues);
                    continue;
                }

                if (RequiresPlayServModuleRegistration(module) &&
                    !moduleRegistrations.ContainsKey(module.Id))
                {
                    issues.Add(new PlayServModuleValidationIssue(
                        PlayServModuleValidationSeverity.Error,
                        "Enabled module assembly does not register an IPlayServModule.",
                        module.Id,
                        module.RootAssemblyReference));
                }
            }
        }

        private static bool RequiresPlayServModuleRegistration(PlayServModuleManifestEntry module)
        {
            if (module == null || string.IsNullOrWhiteSpace(module.RootAssemblyReference))
                return false;

            return !IsTransportModule(module);
        }

        private static bool IsTransportModule(PlayServModuleManifestEntry module)
        {
            return module != null &&
                   (string.Equals(module.Id, PlayServModuleManifest.TransportWebSocketId, StringComparison.Ordinal) ||
                    string.Equals(module.Id, PlayServModuleManifest.TransportUdpId, StringComparison.Ordinal) ||
                    string.Equals(module.Id, PlayServModuleManifest.TransportRudpId, StringComparison.Ordinal) ||
                    string.Equals(module.Id, PlayServModuleManifest.TransportWebRtcId, StringComparison.Ordinal));
        }

        private static void ValidateTransportModuleBootstrap(
            PlayServModuleManifestEntry module,
            IReadOnlyDictionary<string, Assembly> loadedAssemblies,
            List<PlayServModuleValidationIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(module.RootAssemblyReference) ||
                !loadedAssemblies.TryGetValue(module.RootAssemblyReference, out var assembly))
            {
                issues.Add(new PlayServModuleValidationIssue(
                    PlayServModuleValidationSeverity.Error,
                    "Enabled transport module assembly is not loaded.",
                    module.Id,
                    module.RootAssemblyReference));
                return;
            }

            try
            {
                var hasRuntimeBootstrap = assembly.GetTypes()
                    .SelectMany(type => type.GetMethods(
                        BindingFlags.Static |
                        BindingFlags.Public |
                        BindingFlags.NonPublic))
                    .Any(method => method.GetCustomAttributes(
                        typeof(RuntimeInitializeOnLoadMethodAttribute),
                        inherit: false).Length > 0);

                if (!hasRuntimeBootstrap)
                {
                    issues.Add(new PlayServModuleValidationIssue(
                        PlayServModuleValidationSeverity.Error,
                        "Enabled transport module assembly has no runtime registration bootstrap.",
                        module.Id,
                        module.RootAssemblyReference));
                }
            }
            catch (Exception ex)
            {
                issues.Add(new PlayServModuleValidationIssue(
                    PlayServModuleValidationSeverity.Warning,
                    "Could not inspect transport module runtime registration.",
                    module.Id,
                    $"{module.RootAssemblyReference}: {ex.GetBaseException().Message}"));
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

                var hasReference = runtimeReferences.Contains(module.RootAssemblyReference);
                if (hasReference)
                {
                    issues.Add(new PlayServModuleValidationIssue(
                        PlayServModuleValidationSeverity.Error,
                        "Runtime root asmdef references an optional module. Package assemblies must remain immutable.",
                        module.Id,
                        module.RootAssemblyReference));
                }

                if (!moduleAvailable ||
                    !asmdefs.TryGetValue(module.RootAssemblyReference, out var moduleAsmdefPaths) ||
                    moduleAsmdefPaths.Count != 1)
                {
                    continue;
                }

                var moduleAsmdefPath = PlayServPackagePathResolver.ToAbsoluteAssetPath(moduleAsmdefPaths[0]);
                var moduleAsmdef = ReadAsmdef(moduleAsmdefPath, issues);
                if (moduleAsmdef == null)
                    continue;

                var requiredConstraint = "!" + module.DisableDefine;
                if (!(moduleAsmdef.defineConstraints ?? Array.Empty<string>())
                    .Contains(requiredConstraint, StringComparer.Ordinal))
                {
                    issues.Add(new PlayServModuleValidationIssue(
                        PlayServModuleValidationSeverity.Error,
                        "Module asmdef does not exclude the assembly when the module is disabled.",
                        module.Id,
                        $"{module.RootAssemblyReference}: missing {requiredConstraint}"));
                }
            }
        }

        private static void ValidateProjectGeneratedFiles(List<PlayServModuleValidationIssue> issues)
        {
            ValidateProjectGeneratedFile(
                PlayServGeneratedCompatibilityLayer.ProjectGeneratedAsmdefAssetPath,
                requireGeneratedMarker: false,
                issues);
            ValidateProjectGeneratedFile(
                PlayServGeneratedCompatibilityLayer.ProjectModuleSelectionAssetPath,
                requireGeneratedMarker: true,
                issues);
        }

        private static void ValidateProjectGeneratedFile(
            string assetPath,
            bool requireGeneratedMarker,
            List<PlayServModuleValidationIssue> issues)
        {
            var absolutePath = PlayServPackagePathResolver.ToAbsoluteAssetPath(assetPath);
            if (!File.Exists(absolutePath))
            {
                issues.Add(new PlayServModuleValidationIssue(
                    PlayServModuleValidationSeverity.Error,
                    "Project-generated module file is missing. Run module graph sync.",
                    detail: assetPath));
                return;
            }

            if (!requireGeneratedMarker)
                return;

            var text = File.ReadAllText(absolutePath);
            if (text.IndexOf("// <auto-generated />", StringComparison.Ordinal) < 0)
            {
                issues.Add(new PlayServModuleValidationIssue(
                    PlayServModuleValidationSeverity.Warning,
                    "Generated module file does not contain the PlayServ auto-generated marker.",
                    detail: assetPath));
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
            var expectedState = PlayServRuntimeModuleDefines.LoadProjectState();
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
            public string[] defineConstraints;
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
