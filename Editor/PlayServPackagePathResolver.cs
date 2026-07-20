using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using PackageManagerPackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Playserv.Editor
{
    internal static class PlayServPackagePathResolver
    {
        public const string PackageName = "com.playserv.sdk";
        public const string LegacyAssetsFolderName = "playserv-unity-sdk";

        private const string PackagesPrefix = "Packages/";
        private const string AssetsPrefix = "Assets/";

        public static PlayServPackageRoot ResolveRootForScript(string scriptTypeName, string scriptSuffix)
        {
            var rootAssetPath = ResolveRootAssetPathForScript(scriptTypeName, scriptSuffix);
            return new PlayServPackageRoot(rootAssetPath, ToAbsoluteAssetPath(rootAssetPath));
        }

        public static bool TryGetPackageRelativeAssetPath(string assetPath, out string relativePath)
        {
            relativePath = string.Empty;
            assetPath = NormalizeAssetPath(assetPath);
            if (string.IsNullOrEmpty(assetPath))
                return false;

            var packageInfo = FindPackageInfoForAssetPath(assetPath);
            if (packageInfo != null &&
                string.Equals(packageInfo.name, PackageName, StringComparison.OrdinalIgnoreCase) &&
                TryTrimRoot(assetPath, packageInfo.assetPath, out relativePath))
            {
                return true;
            }

            var packageAssetRoot = $"{PackagesPrefix}{PackageName}";
            if (TryTrimRoot(assetPath, packageAssetRoot, out relativePath))
                return true;

            var legacyAssetRoot = $"{AssetsPrefix}{LegacyAssetsFolderName}";
            return TryTrimRoot(assetPath, legacyAssetRoot, out relativePath);
        }

        public static string ToAbsoluteAssetPath(string assetPath)
        {
            assetPath = NormalizeAssetPath(assetPath);
            var packageInfo = FindPackageInfoForAssetPath(assetPath);
            if (packageInfo != null &&
                !string.IsNullOrWhiteSpace(packageInfo.resolvedPath) &&
                assetPath.StartsWith(PackagesPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var packageAssetRoot = NormalizeAssetPath(packageInfo.assetPath);
                if (string.IsNullOrEmpty(packageAssetRoot))
                    packageAssetRoot = $"{PackagesPrefix}{packageInfo.name}";

                if (TryTrimRoot(assetPath, packageAssetRoot, out var packageRelativePath))
                    return Path.GetFullPath(Path.Combine(packageInfo.resolvedPath, packageRelativePath));

                return Path.GetFullPath(packageInfo.resolvedPath);
            }

            return Path.GetFullPath(Path.Combine(ProjectRootPath, assetPath));
        }

        public static string ToProjectAssetPath(string absolutePath)
        {
            var projectRoot = Path.GetFullPath(ProjectRootPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullPath = Path.GetFullPath(absolutePath);
            var relative = IsSameOrChildPath(fullPath, projectRoot)
                ? fullPath.Substring(projectRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                : absolutePath;

            return relative.Replace('\\', '/');
        }

        private static string ResolveRootAssetPathForScript(string scriptTypeName, string scriptSuffix)
        {
            var guids = AssetDatabase.FindAssets($"{scriptTypeName} t:MonoScript");
            string fallbackRootAssetPath = null;
            for (var i = 0; i < guids.Length; i++)
            {
                var assetPath = NormalizeAssetPath(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (!assetPath.EndsWith(scriptSuffix, StringComparison.OrdinalIgnoreCase))
                    continue;

                var rootAssetPath = assetPath.Substring(0, assetPath.Length - scriptSuffix.Length);
                if (rootAssetPath.StartsWith($"{PackagesPrefix}{PackageName}", StringComparison.OrdinalIgnoreCase))
                    return rootAssetPath;

                if (fallbackRootAssetPath == null)
                    fallbackRootAssetPath = rootAssetPath;
            }

            if (!string.IsNullOrEmpty(fallbackRootAssetPath))
                return fallbackRootAssetPath;

            var packageRoot = $"{PackagesPrefix}{PackageName}";
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>($"{packageRoot}/package.json") != null)
                return packageRoot;

            return $"{AssetsPrefix}{LegacyAssetsFolderName}";
        }

        private static PackageManagerPackageInfo FindPackageInfoForAssetPath(string assetPath)
        {
            var packageInfo = PackageManagerPackageInfo.FindForAssetPath(assetPath);
            if (packageInfo != null ||
                !assetPath.StartsWith(PackagesPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return packageInfo;
            }

            return PackageManagerPackageInfo.FindForAssetPath($"{assetPath.TrimEnd('/')}/package.json");
        }

        private static bool TryTrimRoot(string assetPath, string rootAssetPath, out string relativePath)
        {
            assetPath = NormalizeAssetPath(assetPath);
            rootAssetPath = NormalizeAssetPath(rootAssetPath).TrimEnd('/');
            relativePath = string.Empty;

            if (string.Equals(assetPath, rootAssetPath, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!assetPath.StartsWith(rootAssetPath + "/", StringComparison.OrdinalIgnoreCase))
                return false;

            relativePath = assetPath.Substring(rootAssetPath.Length).TrimStart('/');
            return true;
        }

        private static string NormalizeAssetPath(string path)
        {
            return string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : path.Replace('\\', '/').Trim('/');
        }

        private static bool IsSameOrChildPath(string fullPath, string root)
        {
            return string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase) ||
                   fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                   fullPath.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static string ProjectRootPath =>
            Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
    }

    internal sealed class PlayServPackageRoot
    {
        public PlayServPackageRoot(string assetPath, string absolutePath)
        {
            AssetPath = NormalizeAssetPath(assetPath);
            AbsolutePath = Path.GetFullPath(absolutePath);
        }

        public string AssetPath { get; }

        public string AbsolutePath { get; }

        public string ToAbsolutePath(string relativePath)
        {
            return Path.GetFullPath(Path.Combine(AbsolutePath, NormalizeAssetPath(relativePath)));
        }

        public string ToAssetPath(string absolutePath)
        {
            var root = Path.GetFullPath(AbsolutePath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullPath = Path.GetFullPath(absolutePath);
            if (IsSameOrChildPath(fullPath, root))
            {
                var relative = fullPath.Substring(root.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Replace('\\', '/');
                return CombineAssetPath(AssetPath, relative);
            }

            return PlayServPackagePathResolver.ToProjectAssetPath(absolutePath);
        }

        public bool TryGetRelativeAssetPath(string assetPath, out string relativePath)
        {
            return TryTrimRoot(assetPath, AssetPath, out relativePath);
        }

        private static string CombineAssetPath(string rootAssetPath, string relativePath)
        {
            rootAssetPath = NormalizeAssetPath(rootAssetPath);
            relativePath = NormalizeAssetPath(relativePath);
            return string.IsNullOrEmpty(relativePath)
                ? rootAssetPath
                : $"{rootAssetPath}/{relativePath}";
        }

        private static bool TryTrimRoot(string assetPath, string rootAssetPath, out string relativePath)
        {
            assetPath = NormalizeAssetPath(assetPath);
            rootAssetPath = NormalizeAssetPath(rootAssetPath).TrimEnd('/');
            relativePath = string.Empty;

            if (string.Equals(assetPath, rootAssetPath, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!assetPath.StartsWith(rootAssetPath + "/", StringComparison.OrdinalIgnoreCase))
                return false;

            relativePath = assetPath.Substring(rootAssetPath.Length).TrimStart('/');
            return true;
        }

        private static string NormalizeAssetPath(string path)
        {
            return string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : path.Replace('\\', '/').Trim('/');
        }

        private static bool IsSameOrChildPath(string fullPath, string root)
        {
            return string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase) ||
                   fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                   fullPath.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
    }
}
