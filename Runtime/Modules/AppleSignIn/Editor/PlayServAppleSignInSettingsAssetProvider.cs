using System;
using System.Collections.Generic;
using System.IO;
using Playserv.AppleSignIn;
using UnityEditor;
using UnityEngine;

namespace Playserv.Editor.AppleSignIn
{
    internal static class PlayServAppleSignInSettingsAssetProvider
    {
        private const string AssetPath = "Assets/Resources/PlayServAppleSignInSettings.asset";

        public static PlayServAppleSignInSettings GetOrCreate()
        {
            var settings = AssetDatabase.LoadAssetAtPath<PlayServAppleSignInSettings>(AssetPath);
            if (settings != null)
                return settings;

            var folder = Path.GetDirectoryName(AssetPath)?.Replace("\\", "/");
            if (!string.IsNullOrWhiteSpace(folder) && !AssetDatabase.IsValidFolder(folder))
                CreateFolders(folder);

            settings = ScriptableObject.CreateInstance<PlayServAppleSignInSettings>();
            AssetDatabase.CreateAsset(settings, AssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return settings;
        }

        public static PlayServAppleSignInSettings FindExisting()
        {
            var settings = AssetDatabase.LoadAssetAtPath<PlayServAppleSignInSettings>(AssetPath);
            if (settings != null)
                return settings;

            var guids = AssetDatabase.FindAssets("t:PlayServAppleSignInSettings");
            for (var i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                settings = AssetDatabase.LoadAssetAtPath<PlayServAppleSignInSettings>(path);
                if (settings != null)
                    return settings;
            }

            return null;
        }

        private static void CreateFolders(string folderPath)
        {
            var parts = folderPath.Split('/');
            if (parts.Length == 0)
                return;

            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }

    [InitializeOnLoad]
    internal static class PlayServAppleSignInSettingsSecurityMigration
    {
        private const string SessionKey = "PlayServ.AppleSignIn.SanitizeLegacyServerCredentials.v1";

        static PlayServAppleSignInSettingsSecurityMigration()
        {
            EditorApplication.delayCall += Run;
        }

        private static void Run()
        {
            if (SessionState.GetBool(SessionKey, false))
                return;

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += Run;
                return;
            }

            var assetPaths = FindProjectSettingsAssets();
            try
            {
                if (assetPaths.Count > 0)
                    AssetDatabase.ForceReserializeAssets(assetPaths);

                SessionState.SetBool(SessionKey, true);
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[PlayServ] Could not sanitize legacy Apple Sign In settings assets: {ex.GetBaseException().Message}");
            }
        }

        private static List<string> FindProjectSettingsAssets()
        {
            var paths = new List<string>();
            var guids = AssetDatabase.FindAssets("t:PlayServAppleSignInSettings", new[] { "Assets" });
            for (var i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!string.IsNullOrWhiteSpace(path) &&
                    path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                {
                    paths.Add(path);
                }
            }

            return paths;
        }
    }
}
