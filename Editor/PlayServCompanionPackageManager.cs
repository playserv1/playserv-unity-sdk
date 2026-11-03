using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Playserv.Modules;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using PackageManagerPackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Playserv.Editor
{
    [InitializeOnLoad]
    internal static class PlayServCompanionPackageManager
    {
        private const string CorePackageId = "com.playserv.sdk";
        private const string DefaultRepositoryUrl =
            "git@github.com:playserv1/playserv-unity-sdk.git";

        private static AddRequest _addRequest;
        private static RemoveRequest _removeRequest;
        private static string _activePackageId = string.Empty;
        private static string _lastErrorPackageId = string.Empty;
        private static string _lastError = string.Empty;
        private static readonly object OperationOwner = new object();

        static PlayServCompanionPackageManager()
        {
            EditorApplication.update += PollRequests;
        }

        public static bool IsBusy => _addRequest != null || _removeRequest != null || PlayServPackageOperationGate.Shared.IsBusy;

        public static bool IsBusyFor(string packageId)
        {
            return IsBusy &&
                   string.Equals(_activePackageId, packageId, StringComparison.Ordinal);
        }

        public static bool TryGetInstalledPackage(
            string packageId,
            out PackageManagerPackageInfo packageInfo)
        {
            packageInfo = PackageManagerPackageInfo.FindForAssetPath(
                $"Packages/{packageId}/package.json");
            if (packageInfo == null)
            {
                packageInfo = PackageManagerPackageInfo.FindForAssetPath(
                    $"Packages/{packageId}");
            }

            return packageInfo != null;
        }

        public static string GetLastError(string packageId)
        {
            return string.Equals(_lastErrorPackageId, packageId, StringComparison.Ordinal)
                ? _lastError
                : string.Empty;
        }

        public static bool CanRemove(
            PlayServCompanionPackageDefinition package,
            PlayServEditorModuleSettings settings,
            out string reason)
        {
            reason = string.Empty;
            if (package == null)
            {
                reason = "Companion package definition is missing.";
                return false;
            }

            if (IsBusy)
            {
                reason = "A PlayServ Package Manager operation is already running.";
                return false;
            }

            if (!TryGetInstalledPackage(package.PackageId, out var installedPackage))
            {
                reason = $"{package.PackageId} is not installed.";
                return false;
            }

            if (!installedPackage.isDirectDependency)
            {
                reason =
                    $"{package.PackageId} is a transitive dependency. Remove it from the package that declares it.";
                return false;
            }

            if (settings == null)
                return true;

            var packageModuleIds = new HashSet<string>(
                package.ModuleIds,
                StringComparer.Ordinal);
            foreach (var moduleId in package.ModuleIds)
            {
                if (!settings.IsRuntimeModuleEnabled(moduleId))
                    continue;

                var externalDependents = PlayServModuleManifest.RuntimeModules
                    .Where(module =>
                        !packageModuleIds.Contains(module.Id) &&
                        settings.IsRuntimeModuleEnabled(module.Id) &&
                        module.DependencyIds.Contains(
                            moduleId,
                            StringComparer.Ordinal))
                    .Select(module => module.Label)
                    .ToArray();
                if (externalDependents.Length == 0)
                    continue;

                reason =
                    $"Disable dependent modules first: {string.Join(", ", externalDependents)}.";
                return false;
            }

            return true;
        }

        public static bool Install(
            PlayServCompanionPackageDefinition package,
            out string error)
        {
            error = string.Empty;
            if (package == null)
            {
                error = "Companion package definition is missing.";
                return false;
            }

            if (IsBusy)
            {
                error = "A PlayServ Package Manager operation is already running.";
                return false;
            }

            if (TryGetInstalledPackage(package.PackageId, out _))
                return true;

            var corePackage = FindCorePackage();
            if (!TryBuildInstallReference(package, corePackage, out var installReference, out error))
                return false;

            ClearLastError();
            if (!PlayServPackageOperationGate.Shared.TryAcquire(OperationOwner))
            {
                error = "A PlayServ Package Manager operation is already running.";
                return false;
            }
            _activePackageId = package.PackageId;
            Debug.Log($"[PlayServ] Installing {package.PackageId} from {installReference}.");
            try { _addRequest = Client.Add(installReference); }
            catch (Exception)
            {
                PlayServPackageOperationGate.Shared.Release(OperationOwner);
                _activePackageId = string.Empty;
                error = "Package Manager could not start the installation.";
                return false;
            }
            return true;
        }

        public static bool Remove(
            PlayServCompanionPackageDefinition package,
            PlayServEditorModuleSettings settings,
            out string error)
        {
            error = string.Empty;
            if (package == null)
            {
                error = "Companion package definition is missing.";
                return false;
            }

            if (IsBusy)
            {
                error = "A PlayServ Package Manager operation is already running.";
                return false;
            }

            if (!TryGetInstalledPackage(package.PackageId, out _))
                return true;

            if (!CanRemove(package, settings, out error))
                return false;

            if (settings != null)
            {
                var moduleIds = OrderModulesForDisable(package.ModuleIds);
                for (var i = 0; i < moduleIds.Length; i++)
                {
                    var moduleId = moduleIds[i];
                    if (!settings.IsRuntimeModuleEnabled(moduleId))
                        continue;

                    if (!settings.SetRuntimeModuleEnabled(moduleId, false))
                    {
                        error =
                            $"Failed to disable {PlayServModuleManifest.GetLabel(moduleId)} before removing {package.DisplayName}.";
                        return false;
                    }
                }
            }

            ClearLastError();
            if (!PlayServPackageOperationGate.Shared.TryAcquire(OperationOwner))
            {
                error = "A PlayServ Package Manager operation is already running.";
                return false;
            }
            _activePackageId = package.PackageId;
            Debug.Log($"[PlayServ] Removing companion package {package.PackageId}.");
            try { _removeRequest = Client.Remove(package.PackageId); }
            catch (Exception)
            {
                PlayServPackageOperationGate.Shared.Release(OperationOwner);
                _activePackageId = string.Empty;
                error = "Package Manager could not start the removal.";
                return false;
            }
            return true;
        }

        private static string[] OrderModulesForDisable(
            IReadOnlyList<string> packageModuleIds)
        {
            var remaining = new HashSet<string>(
                packageModuleIds ?? Array.Empty<string>(),
                StringComparer.Ordinal);
            var ordered = PlayServModuleManifest.RuntimeModules
                .Where(module => remaining.Remove(module.Id))
                .Select(module => module.Id)
                .Reverse()
                .ToList();
            ordered.AddRange(remaining.OrderBy(moduleId => moduleId, StringComparer.Ordinal));
            return ordered.ToArray();
        }

        internal static bool TryBuildInstallReference(
            PlayServCompanionPackageDefinition package,
            PackageManagerPackageInfo corePackage,
            out string installReference,
            out string error)
        {
            installReference = string.Empty;
            error = string.Empty;

            if (package == null)
            {
                error = "Companion package definition is missing.";
                return false;
            }

            if (corePackage == null)
            {
                var fallbackVersion = PlayServPackageVersionProvider.ResolveInstalledVersion();
                if (string.IsNullOrWhiteSpace(fallbackVersion))
                {
                    error = "Could not resolve the installed PlayServ SDK version.";
                    return false;
                }

                installReference = BuildGitReference(
                    DefaultRepositoryUrl,
                    package.GitPath,
                    fallbackVersion);
                return true;
            }

            switch (corePackage.source)
            {
                case PackageSource.Registry:
                    installReference = BuildRegistryReference(
                        package.PackageId,
                        corePackage.version);
                    return true;

                case PackageSource.Local:
                case PackageSource.Embedded:
                    return TryBuildLocalReference(
                        package,
                        corePackage.resolvedPath,
                        out installReference,
                        out error);

                case PackageSource.Git:
                    return TryBuildGitReference(
                        package,
                        corePackage,
                        out installReference,
                        out error);

                default:
                    error =
                        $"Unsupported core package source '{corePackage.source}'. Add {package.PackageId} to Packages/manifest.json manually.";
                    return false;
            }
        }

        internal static string BuildRegistryReference(
            string packageId,
            string coreVersion)
        {
            return $"{packageId}@{coreVersion}";
        }

        internal static bool TryBuildLocalReference(
            PlayServCompanionPackageDefinition package,
            string coreResolvedPath,
            out string installReference,
            out string error)
        {
            installReference = string.Empty;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(coreResolvedPath))
            {
                error = "The local core package path is unavailable.";
                return false;
            }

            var companionPath = Path.GetFullPath(
                Path.Combine(coreResolvedPath, package.GitPath));
            if (!File.Exists(Path.Combine(companionPath, "package.json")))
            {
                error =
                    $"The core checkout does not contain {package.GitPath}. Install the companion package from Git or a scoped registry.";
                return false;
            }

            installReference = new Uri(companionPath).AbsoluteUri;
            return true;
        }

        internal static string BuildGitReference(
            string repositoryUrl,
            string repositorySubpath,
            string revision)
        {
            var normalizedUrl = StripQueryAndFragment(repositoryUrl);
            var normalizedPath = (repositorySubpath ?? string.Empty)
                .Replace('\\', '/')
                .Trim('/');
            var normalizedRevision = (revision ?? string.Empty).Trim().TrimStart('#');
            return $"{normalizedUrl}?path=/{normalizedPath}#{normalizedRevision}";
        }

        private static PackageManagerPackageInfo FindCorePackage()
        {
            TryGetInstalledPackage(CorePackageId, out var packageInfo);
            return packageInfo;
        }

        private static bool TryBuildGitReference(
            PlayServCompanionPackageDefinition package,
            PackageManagerPackageInfo corePackage,
            out string installReference,
            out string error)
        {
            installReference = string.Empty;
            error = string.Empty;

            var repositoryUrl = corePackage.repository?.url;
            if (string.IsNullOrWhiteSpace(repositoryUrl))
                repositoryUrl = DefaultRepositoryUrl;

            var revision = corePackage.git?.hash;
            if (string.IsNullOrWhiteSpace(revision))
                revision = corePackage.git?.revision;
            if (string.IsNullOrWhiteSpace(revision))
                revision = corePackage.version;

            if (string.IsNullOrWhiteSpace(revision))
            {
                error = "Could not resolve the Git commit used by the core PlayServ package.";
                return false;
            }

            installReference = BuildGitReference(
                repositoryUrl,
                package.GitPath,
                revision);
            return true;
        }

        private static void PollRequests()
        {
            if (_addRequest != null && _addRequest.IsCompleted)
            {
                CompleteAddRequest();
                return;
            }

            if (_removeRequest != null && _removeRequest.IsCompleted)
                CompleteRemoveRequest();
        }

        private static void CompleteAddRequest()
        {
            var packageId = _activePackageId;
            if (_addRequest.Status == StatusCode.Success)
            {
                Debug.Log($"[PlayServ] Installed companion package {packageId}.");
                ClearLastError();
                PlayServSdkCacheMaintenance.QueueTargetedMaintenance();
            }
            else
            {
                SetLastError(
                    packageId,
                    _addRequest.Error?.message ?? "Unknown Package Manager error.");
                Debug.LogError($"[PlayServ] Failed to install {packageId}: {_lastError}");
            }

            _addRequest = null;
            _activePackageId = string.Empty;
            PlayServPackageOperationGate.Shared.Release(OperationOwner);
            RepaintAndRefresh();
        }

        private static void CompleteRemoveRequest()
        {
            var packageId = _activePackageId;
            if (_removeRequest.Status == StatusCode.Success)
            {
                Debug.Log($"[PlayServ] Removed companion package {packageId}.");
                ClearLastError();
                PlayServSdkCacheMaintenance.QueueTargetedMaintenance();
            }
            else
            {
                SetLastError(
                    packageId,
                    _removeRequest.Error?.message ?? "Unknown Package Manager error.");
                Debug.LogError($"[PlayServ] Failed to remove {packageId}: {_lastError}");
            }

            _removeRequest = null;
            _activePackageId = string.Empty;
            PlayServPackageOperationGate.Shared.Release(OperationOwner);
            RepaintAndRefresh();
        }

        private static void RepaintAndRefresh()
        {
            AssetDatabase.Refresh();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        private static void SetLastError(string packageId, string error)
        {
            _lastErrorPackageId = packageId ?? string.Empty;
            _lastError = error ?? string.Empty;
        }

        private static void ClearLastError()
        {
            _lastErrorPackageId = string.Empty;
            _lastError = string.Empty;
        }

        private static string StripQueryAndFragment(string value)
        {
            var result = (value ?? string.Empty).Trim();
            var fragmentIndex = result.IndexOf('#');
            if (fragmentIndex >= 0)
                result = result.Substring(0, fragmentIndex);

            var queryIndex = result.IndexOf('?');
            if (queryIndex >= 0)
                result = result.Substring(0, queryIndex);

            return result;
        }
    }
}
