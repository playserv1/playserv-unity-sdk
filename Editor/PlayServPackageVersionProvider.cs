using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using PackageManagerPackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Playserv.Editor
{
    internal static class PlayServPackageVersionProvider
    {
        private static readonly Regex PackageVersionRegex = new Regex(
            "\"version\"\\s*:\\s*\"([^\"]+)\"",
            RegexOptions.Compiled);

        public static string ResolveInstalledVersion(string fallback = null)
        {
            if (TryResolveInstalledVersion(out var version))
                return version;

            return string.IsNullOrWhiteSpace(fallback) ? string.Empty : fallback.Trim();
        }

        public static bool TryResolveInstalledVersion(out string version)
        {
            version = string.Empty;

            if (TryResolvePackageInfoVersion(out version))
                return true;

            return TryResolvePackageJsonVersion(out version);
        }

        private static bool TryResolvePackageInfoVersion(out string version)
        {
            version = string.Empty;

            var packageInfo = PackageManagerPackageInfo.FindForAssetPath(
                $"Packages/{PlayServPackagePathResolver.PackageName}/package.json");
            if (packageInfo == null)
                packageInfo = PackageManagerPackageInfo.FindForAssetPath($"Packages/{PlayServPackagePathResolver.PackageName}");

            if (packageInfo == null || string.IsNullOrWhiteSpace(packageInfo.version))
                return false;

            version = packageInfo.version.Trim();
            return true;
        }

        private static bool TryResolvePackageJsonVersion(out string version)
        {
            version = string.Empty;

            var packageJsonPath = ResolvePackageJsonPath();
            if (string.IsNullOrWhiteSpace(packageJsonPath) || !File.Exists(packageJsonPath))
                return false;

            var match = PackageVersionRegex.Match(File.ReadAllText(packageJsonPath));
            if (!match.Success || match.Groups.Count < 2)
                return false;

            var resolved = match.Groups[1].Value?.Trim();
            if (string.IsNullOrWhiteSpace(resolved))
                return false;

            version = resolved;
            return true;
        }

        private static string ResolvePackageJsonPath()
        {
            if (PlayServPackagePathResolver.TryResolveRootForScript(
                    nameof(PlayServPackageVersionProvider),
                    "Editor/PlayServPackageVersionProvider.cs",
                    out var root))
            {
                return root.ToAbsolutePath("package.json");
            }

            var projectRoot = Directory.GetParent(UnityEngine.Application.dataPath)?.FullName;
            return string.IsNullOrWhiteSpace(projectRoot)
                ? null
                : Path.Combine(projectRoot, "package.json");
        }
    }
}
