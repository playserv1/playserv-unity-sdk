using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Playserv.Modules;
using UnityEditor;
using UnityEngine;

namespace Playserv.Editor
{
    internal static class PlayServModuleDeleteRestoreStressTest
    {
        private const string ThisScriptSuffix = "/Editor/ModuleStressTests/PlayServModuleDeleteRestoreStressTest.cs";
        private const string RuntimeAsmdefRelativePath = "Runtime/Playserv.Runtime.asmdef";
        private const string CoreEntryPointRelativePath = "Runtime/Core/PlayServ.cs";
        private const string ModuleRegistryRelativePath = "Runtime/Modules/Contracts/PlayServModuleRegistry.cs";
        private const string ModuleManifestRelativePath = "Runtime/Modules/Contracts/PlayServBuiltInModuleManifest.cs";
        private const string ProjectGeneratedAsmdefAssetPath =
            PlayServGeneratedCompatibilityLayer.ProjectGeneratedAsmdefAssetPath;
        private const string ProjectModuleSelectionAssetPath =
            PlayServGeneratedCompatibilityLayer.ProjectModuleSelectionAssetPath;
        private const string EventsApiExtensionsAssetPath = "Assets/Shared/Generated/Events/PlayServ.EventsApiExtensions.g.cs";
        private const string EventsAdapterExtensionsAssetPath = "Assets/Shared/Generated/Events/EventsAdapterExtensions.g.cs";
        private const string PackageSamplesToken = "Playserv.Samples";

        private static readonly TransportFolderExpectation[] TransportFolderExpectations =
        {
            new TransportFolderExpectation(
                PlayServModuleManifest.TransportWebSocketId,
                "Runtime/Proxy/Modules/WebSocket",
                "Playserv.Runtime.Transport.WebSocket"),
            new TransportFolderExpectation(
                PlayServModuleManifest.TransportUdpId,
                "Runtime/Proxy/Modules/Udp",
                "Playserv.Runtime.Transport.Udp"),
            new TransportFolderExpectation(
                PlayServModuleManifest.TransportRudpId,
                "Runtime/Proxy/Modules/Rudp",
                "Playserv.Runtime.Transport.Rudp"),
            new TransportFolderExpectation(
                PlayServModuleManifest.TransportWebRtcId,
                "Runtime/Proxy/Modules/WebRtc",
                "Playserv.Runtime.Transport.WebRtc")
        };

        private static readonly ModuleExpectation[] Expectations =
        {
            new ModuleExpectation(
                PlayServModuleManifest.EventsId,
                "Playserv.Runtime.Modules.Events",
                new[] { "PlayServEvents" },
                new[] { "PlayServEventsModule" }),
            new ModuleExpectation(
                PlayServModuleManifest.DataSubscriptionId,
                "Playserv.Runtime.Modules.DataSubscription",
                new[] { "PlayServData" },
                new[] { "PlayServDataSubscriptionModule" }),
            new ModuleExpectation(
                PlayServModuleManifest.RpcCoreId,
                "Playserv.Runtime.Modules.RPC.Core",
                Array.Empty<string>(),
                new[] { "PlayServRpcCoreModule" }),
            new ModuleExpectation(
                PlayServModuleManifest.ClientRpcId,
                "Playserv.Runtime.Modules.RPC.Client",
                new[] { "PlayServRpc" },
                new[] { "PlayServClientRpcModule" }),
            new ModuleExpectation(
                PlayServModuleManifest.ServerId,
                "Playserv.Runtime.Modules.Server",
                new[] { "PlayServServer", "PlayServServerRpc" },
                new[] { "PlayServServerModule" }),
            new ModuleExpectation(
                PlayServModuleManifest.SpawnId,
                "Playserv.Runtime.Modules.Spawn",
                new[] { "PlayServSpawn", "INetworkPrefabRegistry" },
                new[] { "PlayServSpawnModule" }),
            new ModuleExpectation(
                PlayServModuleManifest.PulseId,
                "Playserv.Runtime.Modules.Pulse",
                Array.Empty<string>(),
                new[] { "PlayServPulseModule" }),
            new ModuleExpectation(
                PlayServModuleManifest.AppleSignInId,
                "Playserv.Runtime.Modules.AppleSignIn",
                Array.Empty<string>(),
                new[] { "PlayServAppleSignInModule" }),
            new ModuleExpectation(
                PlayServModuleManifest.GoogleSignInId,
                "Playserv.Runtime.Modules.GoogleSignIn",
                Array.Empty<string>(),
                new[] { "PlayServGoogleSignInModule" }),
            new ModuleExpectation(
                PlayServModuleManifest.TransportWebSocketId,
                "Playserv.Runtime.Transport.WebSocket",
                Array.Empty<string>(),
                Array.Empty<string>()),
            new ModuleExpectation(
                PlayServModuleManifest.TransportUdpId,
                "Playserv.Runtime.Transport.Udp",
                Array.Empty<string>(),
                Array.Empty<string>()),
            new ModuleExpectation(
                PlayServModuleManifest.TransportRudpId,
                "Playserv.Runtime.Transport.Rudp",
                Array.Empty<string>(),
                Array.Empty<string>()),
            new ModuleExpectation(
                PlayServModuleManifest.TransportWebRtcId,
                "Playserv.Runtime.Transport.WebRtc",
                Array.Empty<string>(),
                Array.Empty<string>())
        };

        [MenuItem("Tools/PlayServ/Modules/Run Delete Restore Stress Test")]
        public static void RunFromMenu()
        {
            try
            {
                var result = Run();
                EditorUtility.DisplayDialog(
                    "PlayServ module stress test",
                    $"Passed. Checked {result.CheckedModules} modules and {result.CheckedTransportFolders} protocol folders.",
                    "OK");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PlayServ] Module delete/restore stress test failed: {ex}");
                EditorUtility.DisplayDialog("PlayServ module stress test failed", ex.Message, "OK");
            }
        }

        [MenuItem("Tools/PlayServ/Modules/Run Delete Restore Stress Test", true)]
        private static bool ValidateRunFromMenu()
        {
            return PlayServEditorModuleAvailability.EditorModuleStressTests &&
                   PlayServProjectModuleSettings.IsEditorToolEnabled(
                       PlayServProjectModuleSettings.ModuleStressTestsEditorToolId,
                       defaultEnabled: false);
        }

        public static void RunFromCli()
        {
            try
            {
                var result = Run();
                Debug.Log($"[PlayServ] Module delete/restore stress test passed. Checked {result.CheckedModules} modules and {result.CheckedTransportFolders} protocol folders.");
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PlayServ] Module delete/restore stress test failed: {ex}");
                EditorApplication.Exit(1);
            }
        }

        internal static StressTestResult Run()
        {
            var packageRoot = PackageRootPath;
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            var backupRoot = Path.Combine(Path.GetTempPath(), "playserv-module-stress-" + Guid.NewGuid().ToString("N"));
            var movedPaths = new List<MovedPath>();
            var buildTargetGroup = EditorUserBuildSettings.selectedBuildTargetGroup;
            var originalDefines = PlayerSettings.GetScriptingDefineSymbolsForGroup(buildTargetGroup);
            GeneratedSnapshot baseline = null;
            var checkedModules = 0;
            var checkedTransportFolders = 0;

            try
            {
                Directory.CreateDirectory(backupRoot);
                PlayServModuleGraphSynchronizer.SyncNow(refreshAssetDatabase: false);
                baseline = GeneratedSnapshot.Capture(packageRoot, projectRoot);
                ValidateJsonManifestCatalog();
                ValidateGeneratedEventsDoNotReferenceSamples(projectRoot);
                ValidateUnavailableModulesDoNotLeak(packageRoot, projectRoot);
                ValidateCoreEntryPointHasNoModuleForwarders(packageRoot);

                foreach (var module in PlayServModuleManifest.RuntimeModules)
                {
                    if (!module.VisibleInExport || module.AssetPaths.Length == 0)
                        continue;

                    if (!PlayServEditorModuleAvailability.TryGetModuleRoot(module, out var moduleRoot) ||
                        !string.Equals(moduleRoot.AbsolutePath, packageRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    MoveModuleOut(moduleRoot.AbsolutePath, backupRoot, module, movedPaths);
                    if (movedPaths.Count == 0)
                        continue;

                    PlayServModuleGraphSynchronizer.SyncNow(refreshAssetDatabase: false);
                    ValidateUnavailableModulesDoNotLeak(packageRoot, projectRoot);
                    ValidateGeneratedEventsDoNotReferenceSamples(projectRoot);
                    ValidateCoreEntryPointHasNoModuleForwarders(packageRoot);
                    GeneratedSnapshot.Capture(packageRoot, projectRoot)
                        .AssertPackageEquals(baseline, packageRoot, $"delete of {module.Label}");

                    RestoreMovedPaths(movedPaths);
                    PlayServModuleGraphSynchronizer.SyncNow(refreshAssetDatabase: false);
                    ValidateUnavailableModulesDoNotLeak(packageRoot, projectRoot);
                    ValidateGeneratedEventsDoNotReferenceSamples(projectRoot);
                    ValidateCoreEntryPointHasNoModuleForwarders(packageRoot);
                    GeneratedSnapshot.Capture(packageRoot, projectRoot)
                        .AssertEquals(baseline, $"restore after {module.Label}");

                    checkedModules++;
                }

                checkedTransportFolders = RunTransportFolderDeleteRestoreScenarios(
                    packageRoot,
                    projectRoot,
                    backupRoot,
                    movedPaths,
                    baseline);

                return new StressTestResult(checkedModules, checkedTransportFolders);
            }
            finally
            {
                RestoreMovedPaths(movedPaths);
                PlayerSettings.SetScriptingDefineSymbolsForGroup(buildTargetGroup, originalDefines);
                PlayServModuleGraphSynchronizer.SyncNow(refreshAssetDatabase: false);

                if (baseline != null)
                    baseline.WriteProjectFilesBack(packageRoot, projectRoot);

                if (Directory.Exists(backupRoot))
                    Directory.Delete(backupRoot, recursive: true);

                AssetDatabase.Refresh();
            }
        }

        private static void MoveModuleOut(
            string packageRoot,
            string backupRoot,
            PlayServModuleManifestEntry module,
            List<MovedPath> movedPaths)
        {
            movedPaths.Clear();

            for (var i = 0; i < module.AssetPaths.Length; i++)
            {
                var relativePath = NormalizeRelativePath(module.AssetPaths[i]);
                if (string.IsNullOrEmpty(relativePath))
                    continue;

                var sourcePath = Path.Combine(packageRoot, relativePath);
                if (!Directory.Exists(sourcePath) && !File.Exists(sourcePath))
                    continue;

                var backupPath = Path.Combine(
                    backupRoot,
                    SanitizePathSegment(module.Id),
                    relativePath.Replace('/', Path.DirectorySeparatorChar));
                var backupParent = Path.GetDirectoryName(backupPath);
                if (!string.IsNullOrEmpty(backupParent))
                    Directory.CreateDirectory(backupParent);

                if (Directory.Exists(sourcePath))
                    Directory.Move(sourcePath, backupPath);
                else
                    File.Move(sourcePath, backupPath);

                movedPaths.Add(new MovedPath(sourcePath, backupPath));
            }
        }

        private static int RunTransportFolderDeleteRestoreScenarios(
            string packageRoot,
            string projectRoot,
            string backupRoot,
            List<MovedPath> movedPaths,
            GeneratedSnapshot baseline)
        {
            var checkedFolders = 0;

            for (var i = 0; i < TransportFolderExpectations.Length; i++)
            {
                var expectation = TransportFolderExpectations[i];
                if (!PlayServModuleManifest.TryGet(expectation.ModuleId, out var module))
                    throw new InvalidOperationException($"Missing manifest entry for protocol folder scenario: {expectation.ModuleId}");

                MovePathOut(packageRoot, backupRoot, module.Id, expectation.RelativePath, movedPaths);
                if (movedPaths.Count == 0)
                    continue;

                PlayServModuleGraphSynchronizer.SyncNow(refreshAssetDatabase: false);
                ValidateProtocolFolderUnavailable(packageRoot, module, expectation);
                ValidateUnavailableModulesDoNotLeak(packageRoot, projectRoot);
                ValidateGeneratedEventsDoNotReferenceSamples(projectRoot);
                ValidateCoreEntryPointHasNoModuleForwarders(packageRoot);
                GeneratedSnapshot.Capture(packageRoot, projectRoot)
                    .AssertPackageEquals(baseline, packageRoot, $"delete of {module.Label} protocol folder");

                RestoreMovedPaths(movedPaths);
                PlayServModuleGraphSynchronizer.SyncNow(refreshAssetDatabase: false);
                ValidateUnavailableModulesDoNotLeak(packageRoot, projectRoot);
                ValidateGeneratedEventsDoNotReferenceSamples(projectRoot);
                ValidateCoreEntryPointHasNoModuleForwarders(packageRoot);
                GeneratedSnapshot.Capture(packageRoot, projectRoot)
                    .AssertEquals(baseline, $"explicit restore after {module.Label} folder");

                checkedFolders++;
            }

            if (checkedFolders != TransportFolderExpectations.Length)
                throw new InvalidOperationException(
                    $"Protocol folder stress test checked {checkedFolders}/{TransportFolderExpectations.Length} folders.");

            return checkedFolders;
        }

        private static void MovePathOut(
            string packageRoot,
            string backupRoot,
            string moduleId,
            string relativePath,
            List<MovedPath> movedPaths)
        {
            movedPaths.Clear();

            relativePath = NormalizeRelativePath(relativePath);
            if (string.IsNullOrEmpty(relativePath))
                return;

            var sourcePath = Path.Combine(packageRoot, relativePath);
            if (!Directory.Exists(sourcePath) && !File.Exists(sourcePath))
                return;

            var backupPath = Path.Combine(
                backupRoot,
                SanitizePathSegment(moduleId),
                "explicit",
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            var backupParent = Path.GetDirectoryName(backupPath);
            if (!string.IsNullOrEmpty(backupParent))
                Directory.CreateDirectory(backupParent);

            if (Directory.Exists(sourcePath))
                Directory.Move(sourcePath, backupPath);
            else
                File.Move(sourcePath, backupPath);

            movedPaths.Add(new MovedPath(sourcePath, backupPath));
        }

        private static void RestoreMovedPaths(List<MovedPath> movedPaths)
        {
            for (var i = movedPaths.Count - 1; i >= 0; i--)
            {
                var movedPath = movedPaths[i];
                if (!Directory.Exists(movedPath.BackupPath) && !File.Exists(movedPath.BackupPath))
                    continue;

                var sourceParent = Path.GetDirectoryName(movedPath.SourcePath);
                if (!string.IsNullOrEmpty(sourceParent))
                    Directory.CreateDirectory(sourceParent);

                if (Directory.Exists(movedPath.BackupPath))
                    Directory.Move(movedPath.BackupPath, movedPath.SourcePath);
                else
                    File.Move(movedPath.BackupPath, movedPath.SourcePath);
            }

            movedPaths.Clear();
        }

        private static void ValidateUnavailableModulesDoNotLeak(string packageRoot, string projectRoot)
        {
            var runtimeAsmdef = ReadPackageFile(packageRoot, RuntimeAsmdefRelativePath);
            var projectSelection = ReadProjectFile(projectRoot, ProjectModuleSelectionAssetPath);

            for (var i = 0; i < Expectations.Length; i++)
            {
                var expectation = Expectations[i];
                if (!PlayServModuleManifest.TryGet(expectation.ModuleId, out var module))
                    continue;

                AssertDoesNotContain(runtimeAsmdef, expectation.AssemblyName, RuntimeAsmdefRelativePath, module.Label);

                if (!PlayServEditorModuleAvailability.IsRuntimeModuleAvailable(module.Label))
                    AssertDoesNotContain(
                        projectSelection,
                        $"\"{module.Id}\"",
                        ProjectModuleSelectionAssetPath,
                        module.Label);
            }
        }

        private static void ValidateJsonManifestCatalog()
        {
            PlayServModuleManifestJsonRegistry.Reload();
            foreach (var diagnostic in PlayServModuleManifestJsonRegistry.Diagnostics)
            {
                if (diagnostic.IsError)
                {
                    throw new InvalidOperationException(
                        $"Invalid module manifest {diagnostic.DescriptorAssetPath}: {diagnostic.Message}");
                }
            }

            var previousOrder = int.MinValue;
            foreach (var module in PlayServModuleManifest.RuntimeModules)
            {
                if (string.IsNullOrWhiteSpace(module.DescriptorAssetPath))
                    throw new InvalidOperationException($"Module is missing {PlayServModuleManifestJsonRegistry.DescriptorFileName}: {module.Id}");

                if (string.IsNullOrWhiteSpace(module.SourceRootAssetPath))
                    throw new InvalidOperationException($"Module descriptor has no package root: {module.Id}");

                var descriptorPath = PlayServPackagePathResolver.ToAbsoluteAssetPath(module.DescriptorAssetPath);
                if (!File.Exists(descriptorPath))
                    throw new InvalidOperationException($"Module descriptor file is missing: {module.DescriptorAssetPath}");

                if (module.Order < previousOrder)
                    throw new InvalidOperationException($"Module descriptor order is unstable at {module.Id}.");

                previousOrder = module.Order;
            }
        }

        private static void ValidateGeneratedEventsDoNotReferenceSamples(string projectRoot)
        {
            var apiExtensions = ReadProjectFile(projectRoot, EventsApiExtensionsAssetPath);
            var adapterExtensions = ReadProjectFile(projectRoot, EventsAdapterExtensionsAssetPath);
            AssertDoesNotContain(apiExtensions, PackageSamplesToken, EventsApiExtensionsAssetPath, "Generated Events API");
            AssertDoesNotContain(adapterExtensions, PackageSamplesToken, EventsAdapterExtensionsAssetPath, "Generated Events adapter");
        }

        private static void ValidateProtocolFolderUnavailable(
            string packageRoot,
            PlayServModuleManifestEntry module,
            TransportFolderExpectation expectation)
        {
            if (PlayServEditorModuleAvailability.IsRuntimeModuleAvailable(module.Label))
                throw new InvalidOperationException($"{module.Label} remained available after deleting {expectation.RelativePath}.");

            var runtimeAsmdef = ReadPackageFile(packageRoot, RuntimeAsmdefRelativePath);
            AssertDoesNotContain(runtimeAsmdef, expectation.AssemblyName, RuntimeAsmdefRelativePath, module.Label);
        }

        private static void ValidateCoreEntryPointHasNoModuleForwarders(string packageRoot)
        {
            var coreEntryPoint = ReadPackageFile(packageRoot, CoreEntryPointRelativePath);
            var forbiddenTokens = new[]
            {
                "PlayServLegacyApiRegistry",
                "IPlayServDataApi",
                "IPlayServEventsApi",
                "IPlayServRpcApi",
                "IPlayServServerRpcApi",
                "IPlayServSpawnApi",
                "public static void Publish",
                "public static void Invoke",
                "public static void Send<",
                "public static void SetRpcInvoker",
                "public static Task<GameObject> Spawn",
                "public static Task<ISharedEntity"
            };

            for (var i = 0; i < forbiddenTokens.Length; i++)
            {
                AssertDoesNotContain(
                    coreEntryPoint,
                    forbiddenTokens[i],
                    CoreEntryPointRelativePath,
                    "Core entry point");
            }
        }

        private static void AssertDoesNotContain(string text, string token, string filePath, string context)
        {
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(text))
                return;

            if (text.IndexOf(token, StringComparison.Ordinal) >= 0)
                throw new InvalidOperationException($"{context} leaked '{token}' in {filePath}.");
        }

        private static string ReadPackageFile(string packageRoot, string relativePath)
        {
            var path = Path.Combine(packageRoot, NormalizeRelativePath(relativePath));
            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }

        private static string ReadProjectFile(string projectRoot, string assetPath)
        {
            var path = Path.Combine(projectRoot, NormalizeRelativePath(assetPath));
            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }

        private static string NormalizeRelativePath(string path)
        {
            return string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : path.Replace('\\', '/').TrimStart('/');
        }

        private static string SanitizePathSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "module";

            var invalid = Path.GetInvalidFileNameChars();
            var chars = value.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                if (invalid.Contains(chars[i]))
                    chars[i] = '_';
            }

            return new string(chars);
        }

        private static string PackageRootPath
        {
            get
            {
                if (!PlayServPackagePathResolver.TryResolveRootForScript(
                    nameof(PlayServModuleDeleteRestoreStressTest),
                    ThisScriptSuffix,
                    out var packageRoot))
                {
                    throw new InvalidOperationException("PlayServ package root was not found.");
                }

                return packageRoot.AbsolutePath;
            }
        }

        internal sealed class StressTestResult
        {
            public StressTestResult(int checkedModules, int checkedTransportFolders)
            {
                CheckedModules = checkedModules;
                CheckedTransportFolders = checkedTransportFolders;
            }

            public int CheckedModules { get; }

            public int CheckedTransportFolders { get; }
        }

        private sealed class GeneratedSnapshot
        {
            private readonly Dictionary<string, string> _files;

            private GeneratedSnapshot(Dictionary<string, string> files)
            {
                _files = files;
            }

            public static GeneratedSnapshot Capture(string packageRoot, string projectRoot)
            {
                return new GeneratedSnapshot(new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [Path.Combine(packageRoot, RuntimeAsmdefRelativePath)] = ReadPackageFile(packageRoot, RuntimeAsmdefRelativePath),
                    [Path.Combine(packageRoot, CoreEntryPointRelativePath)] = ReadPackageFile(packageRoot, CoreEntryPointRelativePath),
                    [Path.Combine(packageRoot, ModuleRegistryRelativePath)] = ReadPackageFile(packageRoot, ModuleRegistryRelativePath),
                    [Path.Combine(packageRoot, ModuleManifestRelativePath)] = ReadPackageFile(packageRoot, ModuleManifestRelativePath),
                    [Path.Combine(projectRoot, ProjectGeneratedAsmdefAssetPath)] = ReadProjectFile(projectRoot, ProjectGeneratedAsmdefAssetPath),
                    [Path.Combine(projectRoot, ProjectModuleSelectionAssetPath)] = ReadProjectFile(projectRoot, ProjectModuleSelectionAssetPath),
                    [Path.Combine(projectRoot, EventsApiExtensionsAssetPath)] = ReadProjectFile(projectRoot, EventsApiExtensionsAssetPath),
                    [Path.Combine(projectRoot, EventsAdapterExtensionsAssetPath)] = ReadProjectFile(projectRoot, EventsAdapterExtensionsAssetPath)
                });
            }

            public void AssertPackageEquals(GeneratedSnapshot expected, string packageRoot, string context)
            {
                foreach (var pair in expected._files)
                {
                    if (!IsSameOrChildPath(pair.Key, packageRoot))
                        continue;

                    if (!_files.TryGetValue(pair.Key, out var actual))
                        actual = string.Empty;

                    if (!string.Equals(actual, pair.Value, StringComparison.Ordinal))
                        throw new InvalidOperationException($"SDK package file changed during {context}: {pair.Key}");
                }
            }

            public void AssertEquals(GeneratedSnapshot expected, string context)
            {
                foreach (var pair in expected._files)
                {
                    if (!_files.TryGetValue(pair.Key, out var actual))
                        actual = string.Empty;

                    if (!string.Equals(actual, pair.Value, StringComparison.Ordinal))
                        throw new InvalidOperationException($"Generated output did not restore to baseline after {context}: {pair.Key}");
                }
            }

            public void WriteProjectFilesBack(string packageRoot, string projectRoot)
            {
                foreach (var pair in _files)
                {
                    if (!IsSameOrChildPath(pair.Key, projectRoot) ||
                        IsSameOrChildPath(pair.Key, packageRoot))
                        continue;

                    var directory = Path.GetDirectoryName(pair.Key);
                    if (!string.IsNullOrEmpty(directory))
                        Directory.CreateDirectory(directory);

                    File.WriteAllText(pair.Key, pair.Value);
                }
            }

            private static bool IsSameOrChildPath(string path, string root)
            {
                var normalizedPath = Path.GetFullPath(path)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var normalizedRoot = Path.GetFullPath(root)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
                       normalizedPath.StartsWith(
                           normalizedRoot + Path.DirectorySeparatorChar,
                           StringComparison.OrdinalIgnoreCase);
            }
        }

        private sealed class MovedPath
        {
            public MovedPath(string sourcePath, string backupPath)
            {
                SourcePath = sourcePath;
                BackupPath = backupPath;
            }

            public string SourcePath { get; }

            public string BackupPath { get; }
        }

        private sealed class ModuleExpectation
        {
            public ModuleExpectation(
                string moduleId,
                string assemblyName,
                string[] compatibilityTokens,
                string[] registryTokens)
            {
                ModuleId = moduleId;
                AssemblyName = assemblyName;
                CompatibilityTokens = compatibilityTokens ?? Array.Empty<string>();
                RegistryTokens = registryTokens ?? Array.Empty<string>();
            }

            public string ModuleId { get; }

            public string AssemblyName { get; }

            public string[] CompatibilityTokens { get; }

            public string[] RegistryTokens { get; }
        }

        private sealed class TransportFolderExpectation
        {
            public TransportFolderExpectation(
                string moduleId,
                string relativePath,
                string assemblyName)
            {
                ModuleId = moduleId ?? throw new ArgumentNullException(nameof(moduleId));
                RelativePath = relativePath ?? throw new ArgumentNullException(nameof(relativePath));
                AssemblyName = assemblyName ?? throw new ArgumentNullException(nameof(assemblyName));
            }

            public string ModuleId { get; }

            public string RelativePath { get; }

            public string AssemblyName { get; }
        }
    }
}
