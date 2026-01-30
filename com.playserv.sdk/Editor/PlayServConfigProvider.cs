#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Playserv.Wrapper;

namespace Playserv.Editor
{
    internal static class PlayServConfigProvider
    {
        // You can change this path if you want a different location.
        private const string AssetPath = "Assets/Resources/PlayServConfig.asset";

        public static PlayServConfig GetOrCreate()
        {
            var config = AssetDatabase.LoadAssetAtPath<PlayServConfig>(AssetPath);
            if (config != null)
                return config;

            // Ensure folder exists
            var folder = System.IO.Path.GetDirectoryName(AssetPath)?.Replace("\\", "/");
            if (!string.IsNullOrWhiteSpace(folder) && !AssetDatabase.IsValidFolder(folder))
            {
                CreateFolders(folder);
            }

            config = ScriptableObject.CreateInstance<PlayServConfig>();
            AssetDatabase.CreateAsset(config, AssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return config;
        }

        public static PlayServConfig FindExisting()
        {
            // Fast-path: direct path
            var atPath = AssetDatabase.LoadAssetAtPath<PlayServConfig>(AssetPath);
            if (atPath != null)
                return atPath;

            // Scan project
            var guids = AssetDatabase.FindAssets("t:PlayServConfig");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<PlayServConfig>(path);
                if (asset != null)
                    return asset;
            }

            return null;
        }

        private static void CreateFolders(string folderPath)
        {
            // folderPath like "Assets/PlayServ/Sub"
            var parts = folderPath.Split('/');
            if (parts.Length == 0) return;

            string current = parts[0]; // "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
#endif