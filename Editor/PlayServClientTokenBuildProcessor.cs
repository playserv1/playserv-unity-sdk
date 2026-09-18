using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Playserv.Runtime.Abstractions;
using Playserv.Wrapper;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Playserv.Editor
{
    [InitializeOnLoad]
    internal sealed class PlayServClientTokenBuildProcessor : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        [Serializable]
        private sealed class Journal
        {
            public int version = 1;
            public List<Entry> entries = new List<Entry>();
        }

        [Serializable]
        private sealed class Entry
        {
            public string guid;
            public string original;
            public string injectedHash;
        }

        private static bool _recoveryWarning;
        internal static string JournalPath => Path.GetFullPath("Library/PlayServ/ClientTokenBuild.json");
        public int callbackOrder => -999;

        static PlayServClientTokenBuildProcessor()
        {
            EditorApplication.delayCall += RecoverWhenIdle;
            EditorApplication.update += RecoverWhenIdle;
            EditorApplication.quitting += RecoverWhenIdle;
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            try
            {
                Restore();
                var journal = new Journal();
                var configs = new List<PlayServConfig>();
                var tokens = new List<string>();
                foreach (var guid in AssetDatabase.FindAssets("t:PlayServConfig", new[] { "Assets" }))
                {
                    var config = AssetDatabase.LoadAssetAtPath<PlayServConfig>(AssetDatabase.GUIDToAssetPath(guid));
                    if (!PlayServEnvironmentClientTokens.IsManaged(config))
                        continue;
                    var token = PlayServCredentialPolicy.NormalizeClientToken(config.ClientToken);
                    if (string.IsNullOrEmpty(token))
                    {
                        var path = AssetDatabase.GetAssetPath(config);
                        if (path.EndsWith("/Resources/PlayServConfig.asset", StringComparison.OrdinalIgnoreCase) &&
                            path.IndexOf("/Editor/", StringComparison.OrdinalIgnoreCase) < 0)
                            throw new BuildFailedException("PlayServ: configure a public Client Token for the active environment before building.");
                        continue;
                    }
                    journal.entries.Add(new Entry
                    {
                        guid = guid,
                        original = config.SerializedClientToken,
                        injectedHash = Hash(token)
                    });
                    configs.Add(config);
                    tokens.Add(token);
                }
                if (configs.Count == 0)
                    return;

                Directory.CreateDirectory(Path.GetDirectoryName(JournalPath));
                File.WriteAllText(JournalPath + ".tmp", JsonUtility.ToJson(journal), new UTF8Encoding(false));
                File.Move(JournalPath + ".tmp", JournalPath);
                for (var index = 0; index < configs.Count; index++)
                {
                    configs[index].SetSerializedClientToken(tokens[index]);
                    SaveConfig(configs[index]);
                }
            }
            catch
            {
                TryRestore();
                throw new BuildFailedException(
                    "PlayServ Client Token build preparation failed. Check the active public key, the paired " +
                    "PLAYSERV_ENVIRONMENT/PLAYSERV_CLIENT_TOKEN overrides and Library/PlayServ recovery state.");
            }
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            try { Restore(); }
            catch
            {
                throw new BuildFailedException("PlayServ Client Token build recovery failed. " +
                    "Keep Library/PlayServ and restore access to the journal/config assets.");
            }
        }

        internal static void RecoverWhenIdle()
        {
            RecoverWhenIdle(BuildPipeline.isBuildingPlayer, EditorApplication.isCompiling, EditorApplication.isUpdating);
        }

        internal static void RecoverWhenIdle(bool isBuildingPlayer, bool isCompiling, bool isUpdating)
        {
            if (!isBuildingPlayer && !isCompiling && !isUpdating)
                TryRestore();
        }

        private static void TryRestore()
        {
            try
            {
                Restore();
                _recoveryWarning = false;
            }
            catch
            {
                if (_recoveryWarning)
                    return;
                _recoveryWarning = true;
                Debug.LogError("[PlayServ] Client Token build recovery could not finish. Keep Library/PlayServ " +
                               "and restore access to the journal/config assets before building again.");
            }
        }

        internal static void Restore()
        {
            if (!File.Exists(JournalPath))
                return;
            var journal = JsonUtility.FromJson<Journal>(File.ReadAllText(JournalPath));
            if (journal == null || journal.version != 1 || journal.entries == null)
                throw new InvalidOperationException("Invalid PlayServ build recovery journal.");

            foreach (var entry in journal.entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.guid) || string.IsNullOrEmpty(entry.injectedHash))
                    throw new InvalidOperationException("Invalid PlayServ build recovery entry.");
                var path = AssetDatabase.GUIDToAssetPath(entry.guid);
                if (string.IsNullOrEmpty(path))
                    continue;
                if (!path.StartsWith("Assets/", StringComparison.Ordinal))
                    throw new InvalidOperationException("PlayServ build recovery target must be a project asset.");
                var config = AssetDatabase.LoadAssetAtPath<PlayServConfig>(path);
                if (config == null)
                    throw new InvalidOperationException("PlayServ build recovery target is not a config asset.");
                if (!string.Equals(config.SerializedClientToken, entry.original, StringComparison.Ordinal))
                {
                    if (Hash(config.SerializedClientToken) != entry.injectedHash)
                    {
                        SaveConfig(config);
                        Debug.LogWarning("[PlayServ] Client Token changed after build preparation; preserving the newer edit. " +
                                         "Review the config asset before committing it.");
                        continue;
                    }
                    config.SetSerializedClientToken(entry.original);
                }
                // A previous recovery may have restored memory but failed to persist it.
                SaveConfig(config);
            }
            File.Delete(JournalPath);
        }

        private static void SaveConfig(PlayServConfig config)
        {
            AssetDatabase.SaveAssetIfDirty(config);
            if (EditorUtility.IsDirty(config))
                throw new IOException("PlayServ config could not be saved; build recovery state is retained.");
        }

        private static string Hash(string value)
        {
            using (var sha = SHA256.Create())
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty)));
        }
    }
}
