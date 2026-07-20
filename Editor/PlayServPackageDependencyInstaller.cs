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

        static PlayServPackageDependencyInstaller()
        {
            EditorApplication.delayCall += EnsureAssetsModeDependencies;
        }

        [MenuItem("Tools/PlayServ/Dependencies/Install Newtonsoft Json")]
        public static void InstallNewtonsoftJson()
        {
            StartInstall(force: true);
        }

        private static void EnsureAssetsModeDependencies()
        {
            if (SessionState.GetBool(AutoInstallSessionKey, false))
                return;

            SessionState.SetBool(AutoInstallSessionKey, true);

            if (!IsAssetsModeSdk())
                return;

            StartInstall(force: false);
        }

        private static void StartInstall(bool force)
        {
            if (_listRequest != null || _addRequest != null)
                return;

            if (!force && !IsAssetsModeSdk())
                return;

            _listRequest = Client.List(false, true);
            EditorApplication.update += WaitForListRequest;
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
            _addRequest = Client.Add($"{PackageName}@{PackageVersion}");
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
