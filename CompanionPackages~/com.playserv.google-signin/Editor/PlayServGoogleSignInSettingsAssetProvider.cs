using System.IO;
using Playserv.GoogleSignIn;
using UnityEditor;
using UnityEngine;

namespace Playserv.Editor.GoogleSignIn
{
    internal static class PlayServGoogleSignInSettingsAssetProvider
    {
        private const string AssetPath = "Assets/Resources/PlayServGoogleSignInSettings.asset";

        public static PlayServGoogleSignInSettings GetOrCreate()
        {
            var settings = AssetDatabase.LoadAssetAtPath<PlayServGoogleSignInSettings>(AssetPath);
            if (settings != null)
                return settings;

            var folder = Path.GetDirectoryName(AssetPath)?.Replace("\\", "/");
            if (!string.IsNullOrWhiteSpace(folder) && !AssetDatabase.IsValidFolder(folder))
                CreateFolders(folder);

            settings = ScriptableObject.CreateInstance<PlayServGoogleSignInSettings>();
            AssetDatabase.CreateAsset(settings, AssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return settings;
        }

        public static PlayServGoogleSignInSettings FindExisting()
        {
            var settings = AssetDatabase.LoadAssetAtPath<PlayServGoogleSignInSettings>(AssetPath);
            if (settings != null)
                return settings;

            var guids = AssetDatabase.FindAssets("t:PlayServGoogleSignInSettings");
            for (var i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                settings = AssetDatabase.LoadAssetAtPath<PlayServGoogleSignInSettings>(path);
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
}
