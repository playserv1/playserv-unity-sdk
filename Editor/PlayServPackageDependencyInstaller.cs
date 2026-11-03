using System;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace Playserv.Editor
{
    [InitializeOnLoad]
    internal static class PlayServPackageDependencyInstaller
    {
        private const string AutoInstallSessionKey = "PlayServ.NewtonsoftJsonDependency.AutoInstallAttempted";
        private const string PackageName = "com.unity.nuget.newtonsoft-json";
        private const string PackageVersion = "3.2.2";
        private const string ThisScriptSuffix = "Editor/PlayServPackageDependencyInstaller.cs";

        private static ListRequest _listRequest;
        private static AddRequest _addRequest;
        private static readonly object OperationOwner = new object();

        static PlayServPackageDependencyInstaller()
        {
            EditorApplication.delayCall += EnsureAssetsModeDependencies;
        }

        [MenuItem("Tools/PlayServ/Dependencies/Install Newtonsoft Json")]
        public static void InstallNewtonsoftJson()
        {
            StartInstall();
        }

        private static void EnsureAssetsModeDependencies()
        {
            if (SessionState.GetBool(AutoInstallSessionKey, false))
                return;

            if (!IsAssetsModeSdk())
                return;

            QueueAutomaticInstall();
        }

        internal static void QueueAutomaticInstall()
        {
            if (SessionState.GetBool(AutoInstallSessionKey, false)) return;
            EditorApplication.update -= TryAutomaticInstall;
            EditorApplication.update += TryAutomaticInstall;
            TryAutomaticInstall();
        }

        internal static void TryAutomaticInstall()
        {
            if (!SessionState.GetBool(AutoInstallSessionKey, false))
            {
                if (!StartInstall()) return;
                SessionState.SetBool(AutoInstallSessionKey, true);
            }
            EditorApplication.update -= TryAutomaticInstall;
        }

        private static bool StartInstall()
        {
            if (_listRequest != null || _addRequest != null)
                return true;

            if (!PlayServPackageOperationGate.Shared.TryAcquire(OperationOwner))
                return false;
            try { _listRequest = Client.List(false, true); }
            catch (Exception)
            {
                PlayServPackageOperationGate.Shared.Release(OperationOwner);
                Debug.LogWarning("[PlayServ] Package Manager could not start dependency inspection.");
                return true;
            }
            EditorApplication.update += WaitForListRequest;
            return true;
        }

        private static void WaitForListRequest()
        {
            if (_listRequest == null || !_listRequest.IsCompleted)
                return;

            EditorApplication.update -= WaitForListRequest;

            if (_listRequest.Status == StatusCode.Success)
            {
                foreach (var package in _listRequest.Result)
                {
                    if (string.Equals(package.name, PackageName, StringComparison.Ordinal))
                    {
                        _listRequest = null;
                        PlayServPackageOperationGate.Shared.Release(OperationOwner);
                        return;
                    }
                }
            }
            else if (_listRequest.Error != null)
            {
                Debug.LogWarning($"[PlayServ] Failed to inspect Package Manager dependencies: {_listRequest.Error.message}");
            }

            _listRequest = null;
            Debug.Log($"[PlayServ] Installing required Unity package {PackageName}@{PackageVersion}.");
            try { _addRequest = Client.Add($"{PackageName}@{PackageVersion}"); }
            catch (Exception)
            {
                PlayServPackageOperationGate.Shared.Release(OperationOwner);
                Debug.LogWarning("[PlayServ] Package Manager could not start dependency installation.");
                return;
            }
            EditorApplication.update += WaitForAddRequest;
        }

        private static void WaitForAddRequest()
        {
            if (_addRequest == null || !_addRequest.IsCompleted)
                return;

            EditorApplication.update -= WaitForAddRequest;

            if (_addRequest.Status == StatusCode.Success)
            {
                Debug.Log($"[PlayServ] Installed {PackageName}@{PackageVersion}.");
            }
            else if (_addRequest.Error != null)
            {
                Debug.LogError(
                    $"[PlayServ] Failed to install {PackageName}@{PackageVersion}: {_addRequest.Error.message}\n" +
                    $"Add \"{PackageName}\": \"{PackageVersion}\" to Packages/manifest.json manually.");
            }

            _addRequest = null;
            PlayServPackageOperationGate.Shared.Release(OperationOwner);
        }

        private static bool IsAssetsModeSdk()
        {
            if (!PlayServPackagePathResolver.TryResolveRootForScript(
                    nameof(PlayServPackageDependencyInstaller),
                    ThisScriptSuffix,
                    out var root))
            {
                return false;
            }

            return root.AssetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase);
        }
    }
}
