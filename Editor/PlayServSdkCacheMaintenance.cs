using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;
using PackageManagerPackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Playserv.Editor
{
    [InitializeOnLoad]
    internal static class PlayServSdkCacheMaintenance
    {
        internal const int CurrentCacheSchemaVersion = 1;

        private const string StatePath = "Library/PlayServ/sdk-cache-state.json";
        private const string SharedCodegenCachePath = "Library/SharedCodegen";
        private const string PlayServCachePath = "Library/PlayServ/Cache";
        private const string PackageCachePath = "Library/PackageCache";
        private const string ProjectGeneratedAssetPath = "Assets/PlayServ/Generated/Runtime";
        private const string PendingMaintenanceSessionKey =
            "PlayServ.CacheMaintenance.Pending";

        private static readonly string[] ManagedPackageIds =
            new[] { "com.playserv.sdk", "com.playserv.schema-tool" }
                .Concat(PlayServCompanionPackageCatalog.All.Select(package => package.PackageId))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

        static PlayServSdkCacheMaintenance()
        {
            EditorApplication.delayCall += RunAutomaticMaintenance;
        }

        public static void QueueTargetedMaintenance()
        {
            SessionState.SetBool(PendingMaintenanceSessionKey, true);
            EditorApplication.delayCall -= RunAutomaticMaintenance;
            EditorApplication.delayCall += RunAutomaticMaintenance;
        }

        [MenuItem("Tools/PlayServ/Cache/Clear PlayServ Cache")]
        private static void ClearPlayServCache()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorUtility.DisplayDialog(
                    "PlayServ cache",
                    "Wait for Unity compilation and Package Manager operations to finish.",
                    "OK");
                return;
            }

            var report = PerformTargetedMaintenance();
            WriteCurrentState();
            RefreshGeneratedState();
            EditorUtility.DisplayDialog(
                "PlayServ cache cleared",
                report.BuildSummary(),
                "OK");
        }

        [MenuItem("Tools/PlayServ/Cache/Rebuild Project Library...")]
        private static void RebuildProjectLibrary()
        {
            if (Application.isBatchMode)
            {
                Debug.LogError("[PlayServ] Full Library rebuild is unavailable in batch mode.");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            if (!EditorUtility.DisplayDialog(
                    "Rebuild Unity Library?",
                    "Unity will close, delete this project's entire Library folder, and reopen the project. The next import can take several minutes.",
                    "Rebuild and restart",
                    "Cancel"))
            {
                return;
            }

            AssetDatabase.SaveAssets();

            try
            {
                var projectRoot = GetProjectRoot();
                var unityExecutable = ResolveUnityExecutable();
                var helperPath = CreateLibraryRebuildHelper(
                    projectRoot,
                    unityExecutable,
                    Process.GetCurrentProcess().Id);
                StartLibraryRebuildHelper(helperPath);
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    "Library rebuild failed",
                    exception.GetBaseException().Message,
                    "OK");
            }
        }

        internal static bool IsStateCurrent(
            PlayServSdkCacheState state,
            string sdkVersion)
        {
            return state != null &&
                   state.schemaVersion == CurrentCacheSchemaVersion &&
                   string.Equals(
                       state.sdkVersion,
                       sdkVersion ?? string.Empty,
                       StringComparison.Ordinal);
        }

        internal static string[] FindStalePackageCacheDirectories(
            string packageCacheRoot,
            IEnumerable<string> installedResolvedPaths)
        {
            if (string.IsNullOrWhiteSpace(packageCacheRoot) ||
                !Directory.Exists(packageCacheRoot))
            {
                return Array.Empty<string>();
            }

            var installedPaths = new HashSet<string>(
                (installedResolvedPaths ?? Array.Empty<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(NormalizeAbsolutePath),
                PathComparison);

            return Directory.GetDirectories(packageCacheRoot)
                .Where(IsManagedPackageCacheDirectory)
                .Where(path => !installedPaths.Contains(NormalizeAbsolutePath(path)))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }

        internal static bool ShouldSkipAutomaticMaintenance(
            bool isBatchMode,
            IEnumerable<string> commandLineArguments)
        {
            if (!isBatchMode)
                return false;

            return (commandLineArguments ?? Array.Empty<string>()).Any(argument =>
                string.Equals(argument, "-runTests", StringComparison.OrdinalIgnoreCase));
        }

        private static void RunAutomaticMaintenance()
        {
            if (ShouldSkipAutomaticMaintenance(
                    Application.isBatchMode,
                    Environment.GetCommandLineArgs()))
            {
                return;
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall -= RunAutomaticMaintenance;
                EditorApplication.delayCall += RunAutomaticMaintenance;
                return;
            }

            var sdkVersion = PlayServPackageVersionProvider.ResolveInstalledVersion();
            var pending = SessionState.GetBool(PendingMaintenanceSessionKey, false);
            var state = ReadState();
            if (!pending && IsStateCurrent(state, sdkVersion))
                return;

            SessionState.EraseBool(PendingMaintenanceSessionKey);
            var report = PerformTargetedMaintenance();
            WriteState(new PlayServSdkCacheState
            {
                schemaVersion = CurrentCacheSchemaVersion,
                sdkVersion = sdkVersion ?? string.Empty
            });
            RefreshGeneratedState();

            if (report.HasChanges)
            {
                Debug.Log(
                    $"[PlayServ] SDK cache maintenance completed for {sdkVersion}. " +
                    report.BuildSummary());
            }
        }

        private static PlayServSdkCacheMaintenanceReport PerformTargetedMaintenance()
        {
            var report = new PlayServSdkCacheMaintenanceReport();
            report.DeletedDirectories += DeleteDirectoryIfExists(SharedCodegenCachePath);
            report.DeletedDirectories += DeleteDirectoryIfExists(PlayServCachePath);

            if (AssetDatabase.IsValidFolder(ProjectGeneratedAssetPath) &&
                AssetDatabase.DeleteAsset(ProjectGeneratedAssetPath))
            {
                report.DeletedGeneratedAssets++;
            }

            var installedPaths = ManagedPackageIds
                .Select(FindInstalledPackage)
                .Where(package => package != null)
                .Select(package => package.resolvedPath)
                .ToArray();
            var stalePackageDirectories = FindStalePackageCacheDirectories(
                PackageCachePath,
                installedPaths);

            for (var i = 0; i < stalePackageDirectories.Length; i++)
                report.DeletedPackageCaches += DeleteDirectoryIfExists(
                    stalePackageDirectories[i]);

            return report;
        }

        private static void RefreshGeneratedState()
        {
            AssetDatabase.Refresh();
            PlayServModuleGraphSynchronizer.QueueSync();
            CompilationPipeline.RequestScriptCompilation();
        }

        private static int DeleteDirectoryIfExists(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return 0;

            try
            {
                Directory.Delete(path, recursive: true);
                return 1;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"[PlayServ] Could not delete cache directory '{path}': " +
                    exception.GetBaseException().Message);
                return 0;
            }
        }

        private static PlayServSdkCacheState ReadState()
        {
            if (!File.Exists(StatePath))
                return null;

            try
            {
                return JsonUtility.FromJson<PlayServSdkCacheState>(
                    File.ReadAllText(StatePath));
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[PlayServ] Could not read SDK cache state: " +
                    exception.GetBaseException().Message);
                return null;
            }
        }

        private static void WriteCurrentState()
        {
            WriteState(new PlayServSdkCacheState
            {
                schemaVersion = CurrentCacheSchemaVersion,
                sdkVersion = PlayServPackageVersionProvider.ResolveInstalledVersion()
            });
        }

        private static void WriteState(PlayServSdkCacheState state)
        {
            try
            {
                var directory = Path.GetDirectoryName(StatePath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllText(
                    StatePath,
                    JsonUtility.ToJson(state, prettyPrint: true) + Environment.NewLine);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[PlayServ] Could not write SDK cache state: " +
                    exception.GetBaseException().Message);
            }
        }

        private static bool IsManagedPackageCacheDirectory(string path)
        {
            var directoryName = Path.GetFileName(
                path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            for (var i = 0; i < ManagedPackageIds.Length; i++)
            {
                var packageId = ManagedPackageIds[i];
                if (string.Equals(directoryName, packageId, StringComparison.Ordinal) ||
                    directoryName.StartsWith(packageId + "@", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static PackageManagerPackageInfo FindInstalledPackage(
            string packageId)
        {
            var packageInfo = PackageManagerPackageInfo.FindForAssetPath(
                $"Packages/{packageId}/package.json");
            return packageInfo ??
                   PackageManagerPackageInfo.FindForAssetPath(
                       $"Packages/{packageId}");
        }

        private static string NormalizeAbsolutePath(string path)
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static StringComparer PathComparison =>
            Application.platform == RuntimePlatform.WindowsEditor
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;

        private static string GetProjectRoot()
        {
            return Directory.GetParent(Application.dataPath)?.FullName ??
                   throw new InvalidOperationException("Could not resolve the Unity project root.");
        }

        private static string ResolveUnityExecutable()
        {
            var processPath = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(processPath) && File.Exists(processPath))
                return processPath;

            var applicationPath = EditorApplication.applicationPath;
            if (Application.platform == RuntimePlatform.OSXEditor &&
                applicationPath.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            {
                applicationPath = Path.Combine(
                    applicationPath,
                    "Contents",
                    "MacOS",
                    "Unity");
            }

            if (!File.Exists(applicationPath))
                throw new FileNotFoundException("Could not resolve the Unity executable.", applicationPath);

            return applicationPath;
        }

        private static string CreateLibraryRebuildHelper(
            string projectRoot,
            string unityExecutable,
            int unityProcessId)
        {
            var helperDirectory = Path.Combine(
                Path.GetTempPath(),
                "PlayServ",
                "LibraryRebuild");
            Directory.CreateDirectory(helperDirectory);

            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                var helperPath = Path.Combine(
                    helperDirectory,
                    $"rebuild-{Guid.NewGuid():N}.ps1");
                File.WriteAllText(
                    helperPath,
                    BuildPowerShellRebuildScript(
                        projectRoot,
                        unityExecutable,
                        unityProcessId));
                return helperPath;
            }

            var shellHelperPath = Path.Combine(
                helperDirectory,
                $"rebuild-{Guid.NewGuid():N}.sh");
            File.WriteAllText(
                shellHelperPath,
                BuildShellRebuildScript(
                    projectRoot,
                    unityExecutable,
                    unityProcessId));
            return shellHelperPath;
        }

        private static void StartLibraryRebuildHelper(string helperPath)
        {
            ProcessStartInfo processInfo;
            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                processInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments =
                        $"-NoProfile -ExecutionPolicy Bypass -File {QuoteProcessArgument(helperPath)}",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
            }
            else
            {
                processInfo = new ProcessStartInfo
                {
                    FileName = "/bin/sh",
                    Arguments = QuoteProcessArgument(helperPath),
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
            }

            Process.Start(processInfo);
        }

        private static string BuildShellRebuildScript(
            string projectRoot,
            string unityExecutable,
            int unityProcessId)
        {
            var libraryPath = Path.Combine(projectRoot, "Library");
            var builder = new StringBuilder();
            builder.AppendLine("#!/bin/sh");
            builder.AppendLine($"while kill -0 {unityProcessId} 2>/dev/null; do sleep 1; done");
            builder.AppendLine($"rm -rf -- {QuoteShell(libraryPath)}");
            builder.AppendLine(
                $"{QuoteShell(unityExecutable)} -projectPath {QuoteShell(projectRoot)} >/dev/null 2>&1 &");
            builder.AppendLine("rm -f -- \"$0\"");
            return builder.ToString();
        }

        private static string BuildPowerShellRebuildScript(
            string projectRoot,
            string unityExecutable,
            int unityProcessId)
        {
            var libraryPath = Path.Combine(projectRoot, "Library");
            var builder = new StringBuilder();
            builder.AppendLine(
                $"Wait-Process -Id {unityProcessId} -ErrorAction SilentlyContinue");
            builder.AppendLine(
                $"Remove-Item -LiteralPath {QuotePowerShell(libraryPath)} -Recurse -Force -ErrorAction SilentlyContinue");
            builder.AppendLine(
                $"Start-Process -FilePath {QuotePowerShell(unityExecutable)} -ArgumentList '-projectPath', {QuotePowerShell(projectRoot)}");
            builder.AppendLine(
                "Remove-Item -LiteralPath $MyInvocation.MyCommand.Path -Force -ErrorAction SilentlyContinue");
            return builder.ToString();
        }

        private static string QuoteShell(string value)
        {
            return "'" + (value ?? string.Empty).Replace("'", "'\"'\"'") + "'";
        }

        private static string QuotePowerShell(string value)
        {
            return "'" + (value ?? string.Empty).Replace("'", "''") + "'";
        }

        private static string QuoteProcessArgument(string value)
        {
            return "\"" + (value ?? string.Empty)
                .Replace("\"", "\\\"") + "\"";
        }
    }

    [Serializable]
    internal sealed class PlayServSdkCacheState
    {
        public int schemaVersion;
        public string sdkVersion;
    }

    internal sealed class PlayServSdkCacheMaintenanceReport
    {
        public int DeletedDirectories { get; set; }
        public int DeletedGeneratedAssets { get; set; }
        public int DeletedPackageCaches { get; set; }

        public bool HasChanges =>
            DeletedDirectories > 0 ||
            DeletedGeneratedAssets > 0 ||
            DeletedPackageCaches > 0;

        public string BuildSummary()
        {
            return
                $"Cache directories: {DeletedDirectories}\n" +
                $"Generated SDK folders: {DeletedGeneratedAssets}\n" +
                $"Stale package caches: {DeletedPackageCaches}";
        }
    }
}
