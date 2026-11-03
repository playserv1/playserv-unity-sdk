using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using PackageManagerPackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Playserv.Editor
{
    internal static class PlayServPackageVersionProvider
    {
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

        public static bool TryResolvePackageJsonVersion(out string version)
        {
            version = string.Empty;

            if (!TryResolvePackageJsonPath(out var packageJsonPath))
                return false;

            try
            {
                var manifest = JsonUtility.FromJson<PackageManifest>(File.ReadAllText(packageJsonPath));
                var resolved = manifest?.version?.Trim();
                if (string.IsNullOrWhiteSpace(resolved))
                    return false;

                version = resolved;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static bool TryResolvePackageJsonPath(out string packageJsonPath)
        {
            packageJsonPath = ResolvePackageJsonPath();
            if (string.IsNullOrWhiteSpace(packageJsonPath) || !File.Exists(packageJsonPath))
                return false;

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

        [Serializable]
        private sealed class PackageManifest
        {
            public string version;
        }
    }
}
