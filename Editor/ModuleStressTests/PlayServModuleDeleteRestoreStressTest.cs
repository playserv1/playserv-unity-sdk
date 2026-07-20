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
        private const string CompatibilityRelativePath = "Runtime/Generated/Compatibility/PlayServCompatibility.g.cs";
        private const string ModuleRegistryRelativePath = "Runtime/Generated/Modules/PlayServModuleRegistry.g.cs";
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
                   EditorPrefs.GetBool(Const.PrefModuleStressTests, false);
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
                ValidateGeneratedEventsDoNotReferenceSamples(projectRoot);
                ValidateUnavailableModulesDoNotLeak(packageRoot);
                ValidateSpawnCompatibilityPath(packageRoot);

                foreach (var module in PlayServModuleManifest.RuntimeModules)
                {
                    if (!module.VisibleInExport || module.AssetPaths.Length == 0)
                        continue;

                    MoveModuleOut(packageRoot, backupRoot, module, movedPaths);
                    if (movedPaths.Count == 0)
                        continue;

                    PlayServModuleGraphSynchronizer.SyncNow(refreshAssetDatabase: false);
                    ValidateUnavailableModulesDoNotLeak(packageRoot);
                    ValidateGeneratedEventsDoNotReferenceSamples(projectRoot);
                    ValidateSpawnCompatibilityPath(packageRoot);

                    RestoreMovedPaths(movedPaths);
                    PlayServModuleGraphSynchronizer.SyncNow(refreshAssetDatabase: false);
                    ValidateUnavailableModulesDoNotLeak(packageRoot);
                    ValidateGeneratedEventsDoNotReferenceSamples(projectRoot);
                    ValidateSpawnCompatibilityPath(packageRoot);
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
                    baseline.WriteBack(packageRoot, projectRoot);

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
                ValidateUnavailableModulesDoNotLeak(packageRoot);
                ValidateGeneratedEventsDoNotReferenceSamples(projectRoot);
                ValidateSpawnCompatibilityPath(packageRoot);

                RestoreMovedPaths(movedPaths);
                PlayServModuleGraphSynchronizer.SyncNow(refreshAssetDatabase: false);
                ValidateUnavailableModulesDoNotLeak(packageRoot);
                ValidateGeneratedEventsDoNotReferenceSamples(projectRoot);
                ValidateSpawnCompatibilityPath(packageRoot);
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

        private static void ValidateUnavailableModulesDoNotLeak(string packageRoot)
        {
            var runtimeAsmdef = ReadPackageFile(packageRoot, RuntimeAsmdefRelativePath);
            var compatibility = ReadPackageFile(packageRoot, CompatibilityRelativePath);
            var registry = ReadPackageFile(packageRoot, ModuleRegistryRelativePath);

            for (var i = 0; i < Expectations.Length; i++)
            {
                var expectation = Expectations[i];
                if (!PlayServModuleManifest.TryGet(expectation.ModuleId, out var module))
                    continue;

                if (PlayServEditorModuleAvailability.IsRuntimeModuleAvailable(module.Label))
                    continue;

                AssertDoesNotContain(runtimeAsmdef, expectation.AssemblyName, RuntimeAsmdefRelativePath, module.Label);

                for (var j = 0; j < expectation.CompatibilityTokens.Length; j++)
                    AssertDoesNotContain(compatibility, expectation.CompatibilityTokens[j], CompatibilityRelativePath, module.Label);

                for (var j = 0; j < expectation.RegistryTokens.Length; j++)
                    AssertDoesNotContain(registry, expectation.RegistryTokens[j], ModuleRegistryRelativePath, module.Label);
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

            if (PlayServCoreAssemblyReferenceSync.HasRuntimeModuleReference(module.Id))
                throw new InvalidOperationException($"{module.Label} leaked runtime asmdef reference after deleting {expectation.RelativePath}.");

            var runtimeAsmdef = ReadPackageFile(packageRoot, RuntimeAsmdefRelativePath);
            AssertDoesNotContain(runtimeAsmdef, expectation.AssemblyName, RuntimeAsmdefRelativePath, module.Label);
        }

        private static void ValidateSpawnCompatibilityPath(string packageRoot)
        {
            var compatibility = ReadPackageFile(packageRoot, CompatibilityRelativePath);
            var spawnAvailable = PlayServEditorModuleAvailability.IsRuntimeModuleAvailable("Spawn") &&
                                 PlayServCoreAssemblyReferenceSync.HasRuntimeModuleReference(PlayServModuleManifest.SpawnId);

            AssertDoesNotContain(compatibility, "Type.GetType", CompatibilityRelativePath, "Spawn compatibility");
            AssertDoesNotContain(compatibility, ".GetMethod(", CompatibilityRelativePath, "Spawn compatibility");
            AssertDoesNotContain(compatibility, ".Invoke(null", CompatibilityRelativePath, "Spawn compatibility");
            AssertDoesNotContain(compatibility, ".Invoke(new object", CompatibilityRelativePath, "Spawn compatibility");

            if (!spawnAvailable)
                return;

            if (compatibility.IndexOf("PlayServSpawn.Spawn", StringComparison.Ordinal) < 0 ||
                compatibility.IndexOf("PlayServSpawn.Despawn", StringComparison.Ordinal) < 0)
            {
                throw new InvalidOperationException(
                    "Spawn compatibility must use typed direct PlayServSpawn calls when the Spawn assembly reference is enabled.");
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
                    [Path.Combine(packageRoot, CompatibilityRelativePath)] = ReadPackageFile(packageRoot, CompatibilityRelativePath),
                    [Path.Combine(packageRoot, ModuleRegistryRelativePath)] = ReadPackageFile(packageRoot, ModuleRegistryRelativePath),
                    [Path.Combine(projectRoot, EventsApiExtensionsAssetPath)] = ReadProjectFile(projectRoot, EventsApiExtensionsAssetPath),
                    [Path.Combine(projectRoot, EventsAdapterExtensionsAssetPath)] = ReadProjectFile(projectRoot, EventsAdapterExtensionsAssetPath)
                });
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

            public void WriteBack(string packageRoot, string projectRoot)
            {
                foreach (var pair in _files)
                {
                    var directory = Path.GetDirectoryName(pair.Key);
                    if (!string.IsNullOrEmpty(directory))
                        Directory.CreateDirectory(directory);

                    File.WriteAllText(pair.Key, pair.Value);
                }
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
