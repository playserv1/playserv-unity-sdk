using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor.PackageManager;
using UnityEngine;

namespace Playserv.Editor
{
    [Serializable]
    internal sealed class PlayServPackageSnapshot
    {
        public string Name;
        public string Version;
        public PackageSource Source;
        public bool IsDirect;
        public string PackageId;
        public string RegistryUrl;
    }

    [Serializable]
    internal sealed class PlayServPackageUpdate
    {
        public string Name;
        public string PreviousVersion;
        public string Version;
        public PackageSource Source;
        public string Reference;
        public string RegistryUrl;
        public bool IsDirect;
    }

    /// <summary>Plans an explicit, source-preserving SDK update without changing the project.</summary>
    [Serializable]
    internal sealed class PlayServSdkUpdatePlan
    {
        public const string CoreId = "com.playserv.sdk";
        public static readonly string[] ManagedIds = new[] { CoreId, "com.playserv.schema-tool" }
            .Concat(PlayServCompanionPackageCatalog.All.Select(p => p.PackageId)).Distinct().ToArray();

        public string TargetVersion;
        public string InventoryFingerprint;
        public PlayServPackageUpdate[] Packages;

        public string[] References => Packages.Where(p => p.PreviousVersion != p.Version)
            .Select(p => p.Reference).ToArray();

        public string Describe() => string.Join("\n", Packages.Select(p =>
            p.Name + ": " + p.PreviousVersion + " → " + p.Version));

        public static string SelectLatest(string installed, string[] compatibleVersions)
        {
            if (!TryVersion(installed, out var current)) return null;
            string latest = null;
            foreach (var text in compatibleVersions ?? Array.Empty<string>())
            {
                if (!TryVersion(text, out var version) || version.CompareTo(current) <= 0) continue;
                current = version;
                latest = text;
            }
            return latest;
        }

        public static PlayServSdkUpdatePlan Create(PlayServPackageSnapshot[] installed, string target)
        {
            var core = RequireRegistryCore(installed);
            if (!TryVersion(target, out var targetVersion) || !TryVersion(core.Version, out var coreVersion) ||
                targetVersion.CompareTo(coreVersion) <= 0)
                throw new InvalidOperationException("Choose a newer stable SDK version.");

            var packages = new List<PlayServPackageUpdate>();
            foreach (var package in installed.Where(p => ManagedIds.Contains(p.Name)).OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                if (!TryVersion(package.Version, out var current))
                    throw new InvalidOperationException(package.Name + " is not pinned to a stable release.");
                if (current.CompareTo(targetVersion) > 0)
                    throw new InvalidOperationException(package.Name + " would require a downgrade. Update manually.");
                if (!package.IsDirect && package.Version != target)
                    throw new InvalidOperationException(package.Name + " is controlled by another dependency. Update it there first.");

                string reference;
                if (package.Source == PackageSource.Registry)
                    reference = package.Name + "@" + target;
                else if (package.Source == PackageSource.Git && TryGitReference(package, target, out var gitReference))
                    reference = gitReference;
                else
                    throw new InvalidOperationException(package.Name + " is local, embedded, a fork, or not pinned to an official release tag. Update manually.");

                packages.Add(new PlayServPackageUpdate
                {
                    Name = package.Name, PreviousVersion = package.Version, Version = target,
                    Source = package.Source, Reference = reference, RegistryUrl = package.RegistryUrl,
                    IsDirect = package.IsDirect
                });
            }

            return new PlayServSdkUpdatePlan
            {
                TargetVersion = target, InventoryFingerprint = Fingerprint(installed), Packages = packages.ToArray()
            };
        }

        public static PlayServPackageSnapshot RequireRegistryCore(PlayServPackageSnapshot[] installed)
        {
            var core = installed?.SingleOrDefault(p => p.Name == CoreId);
            if (core == null || core.Source != PackageSource.Registry || !core.IsDirect)
                throw new InvalidOperationException("Updates require a direct registry installation of PlayServ SDK. Git and local SDK copies are not changed.");
            return core;
        }

        public static string Fingerprint(PlayServPackageSnapshot[] installed)
        {
            // Hash the identity, never persist an arbitrary package URL that might contain credentials.
            return Hash128.Compute(string.Join("\n", (installed ?? Array.Empty<PlayServPackageSnapshot>())
                .OrderBy(p => p.Name, StringComparer.Ordinal).Select(JsonUtility.ToJson))).ToString();
        }

        public bool MatchesInstalled(PlayServPackageSnapshot[] installed)
        {
            if (Packages == null || Packages.Length == 0 || !Packages.Any(p => p.Name == CoreId)) return false;
            var actualIds = installed.Where(p => ManagedIds.Contains(p.Name)).Select(p => p.Name).OrderBy(n => n).ToArray();
            if (!actualIds.SequenceEqual(Packages.Select(p => p.Name).OrderBy(n => n))) return false;
            return Packages.All(expected => installed.Any(actual =>
                actual.Name == expected.Name && actual.Version == expected.Version &&
                actual.Source == expected.Source && actual.IsDirect == expected.IsDirect &&
                (actual.Source == PackageSource.Registry
                    ? SameRegistry(actual.RegistryUrl, expected.RegistryUrl)
                    : actual.PackageId == expected.Name + "@" + expected.Reference)));
        }

        public static bool SameRegistry(string first, string second) =>
            !string.IsNullOrEmpty(first) && string.Equals(first.TrimEnd('/'), second?.TrimEnd('/'), StringComparison.Ordinal);

        private static bool TryGitReference(PlayServPackageSnapshot package, string target, out string reference)
        {
            reference = null;
            var prefix = package.Name + "@";
            if (package.PackageId == null || !package.PackageId.StartsWith(prefix, StringComparison.Ordinal)) return false;
            var locator = package.PackageId.Substring(prefix.Length);
            var suffix = "?path=/CompanionPackages~/" + package.Name + "#" + package.Version;
            if (!locator.EndsWith(suffix, StringComparison.Ordinal)) return false;
            var repository = locator.Substring(0, locator.Length - suffix.Length);
            var official = new[]
            {
                "https://github.com/playserv1/playserv-unity-sdk.git",
                "git+https://github.com/playserv1/playserv-unity-sdk.git",
                "git@github.com:playserv1/playserv-unity-sdk.git",
                "ssh://git@github.com/playserv1/playserv-unity-sdk.git"
            };
            if (!official.Contains(repository, StringComparer.Ordinal)) return false;
            reference = repository + "?path=/CompanionPackages~/" + package.Name + "#" + target;
            return true;
        }

        private static bool TryVersion(string value, out Version version)
        {
            version = null;
            var parts = value?.Split('.');
            if (parts == null || parts.Length != 3) return false;
            var numbers = new int[3];
            for (var i = 0; i < 3; i++)
            {
                if (parts[i].Length == 0 || (parts[i].Length > 1 && parts[i][0] == '0') ||
                    parts[i].Any(c => c < '0' || c > '9') ||
                    !int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i])) return false;
            }
            version = new Version(numbers[0], numbers[1], numbers[2]);
            return true;
        }
    }
}
