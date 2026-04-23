// Assets/Editor/SharedCodeGenerator.cs
#nullable enable
#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.Compilation;
using UnityEngine;

namespace Playserv.CodeGenerator.Editor
{
    public static class SharedCodeGenerator
    {
        private const string OutputDir = "Assets/Shared/Generated/DTOs";
        private const string CacheFolder = "Library/SharedCodegen";
        private const string CacheFile = "Library/SharedCodegen/cache.txt";

        private const string PendingKey = "SharedCodeGenerator.Pending";
        private const string RunningKey = "SharedCodeGenerator.Running";
        private const string DirtyAssetsKey = "SharedCodeGenerator.DirtyAssets";
        private const string DeletedAssetsKey = "SharedCodeGenerator.DeletedAssets";

        private const string AutoGenPrefKey = "PlayServ.Codegen.AutoGenerate";

        // Unity 2021.3 compatibility for C# 9 init setters
        private const string IsExternalInitFileName = "IsExternalInit.cs";

        static SharedCodeGenerator()
        {
            UnityEngine.Debug.Log("[PlayServ] SharedCodeGenerator loaded!");
        }
        
        [InitializeOnLoadMethod]
        private static void Init()
        {
            EditorApplication.delayCall += () => Schedule("delayCall");
            CompilationPipeline.compilationFinished += _ => Schedule("compilationFinished");
            EditorApplication.projectChanged += () => Schedule("projectChanged");
        }

        [DidReloadScripts]
        private static void AfterReload()
        {
            Schedule("DidReloadScripts");
        }

        public static void GenerateMenu()
        {
            RunNow("menu");
        }

        internal static void TrackAssetChanges(IEnumerable<string> dirtyAssets, IEnumerable<string> deletedAssets, string reason)
        {
            if (!IsAutoGenerationEnabled())
                return;

            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            MergeTrackedAssets(DirtyAssetsKey, dirtyAssets);
            MergeTrackedAssets(DeletedAssetsKey, deletedAssets);
            Schedule(reason);
        }

        public static void DestroyDTOs()
        {
            AssetDatabase.DeleteAsset(OutputDir);
            AssetDatabase.Refresh();

            try
            {
                var cacheDir = Path.GetDirectoryName(CacheFile) ?? CacheFolder;
                if (Directory.Exists(cacheDir))
                    Directory.Delete(cacheDir, true);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PlayServ] Failed to delete cache folder: {e.Message}");
            }
        }

        private static void Schedule(string reason)
        {
            if (!IsAutoGenerationEnabled())
                return;

            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            SessionState.SetString(PendingKey, reason);

            EditorApplication.delayCall -= RunScheduled;
            EditorApplication.delayCall += RunScheduled;
        }

        private static void RunScheduled()
        {
            if (SessionState.GetBool(RunningKey, false))
                return;

            var reason = SessionState.GetString(PendingKey, "scheduled");
            SessionState.EraseString(PendingKey);
            var dirtyAssets = ConsumeTrackedAssets(DirtyAssetsKey);
            var deletedAssets = ConsumeTrackedAssets(DeletedAssetsKey);

            if (!ShouldRun(reason, dirtyAssets, deletedAssets))
                return;

            SessionState.SetBool(RunningKey, true);
            try
            {
                GenerateAll(reason, dirtyAssets, deletedAssets, ShouldForceFullRebuild(reason));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                SessionState.SetBool(RunningKey, false);
            }
        }

        private static void RunNow(string reason)
        {
            if (SessionState.GetBool(RunningKey, false))
                return;

            SessionState.SetBool(RunningKey, true);
            try
            {
                GenerateAll(reason, new HashSet<string>(StringComparer.OrdinalIgnoreCase), new HashSet<string>(StringComparer.OrdinalIgnoreCase), true);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                SessionState.SetBool(RunningKey, false);
            }
        }

        private static bool IsAutoGenerationEnabled()
        {
            return EditorPrefs.GetBool(AutoGenPrefKey, true);
        }

        private static void GenerateAll(
            string reason,
            HashSet<string> dirtyAssets,
            HashSet<string> deletedAssets,
            bool fullRebuild)
        {
            Directory.CreateDirectory(OutputDir);
            Directory.CreateDirectory(Path.GetDirectoryName(CacheFile) ?? CacheFolder);

            var cache = SimpleCache.Load(CacheFile);

            // Keep outputs so stale cleanup doesn't delete them
            var keepOutputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Ensure IsExternalInit exists for Unity 2021.3 (init setters)
            EnsureIsExternalInit(OutputDir, cache, keepOutputs);

            int parsedFiles = 0;
            int foundBindings = 0;
            int written = 0;

            if (fullRebuild)
            {
                var currentSourceFiles = new HashSet<string>(
                    EnumerateRelevantSourceAssetPaths(),
                    StringComparer.OrdinalIgnoreCase);

                foreach (var cachedPath in cache.FileHashes.Keys.ToArray())
                {
                    if (!currentSourceFiles.Contains(cachedPath))
                        RemoveSourceFile(cache, cachedPath);
                }

                foreach (var assetPath in currentSourceFiles)
                {
                    if (!TryReadAssetText(assetPath, out var text))
                        continue;

                    UpdateSourceSnapshotCache(cache, assetPath, text);
                }
            }
            else
            {
                foreach (var deletedAsset in deletedAssets)
                    RemoveSourceFile(cache, deletedAsset);

                foreach (var dirtyAsset in dirtyAssets)
                {
                    if (!TryReadAssetText(dirtyAsset, out var text))
                    {
                        RemoveSourceFile(cache, dirtyAsset);
                        continue;
                    }

                    UpdateSourceSnapshotCache(cache, dirtyAsset, text);
                }
            }

            var typeSnapshots = cache.FileTypeSnapshots.Values
                .Select(TypeIndexBuilder.DeserializeSnapshot)
                .ToArray();
            var typeIndex = TypeIndexBuilder.BuildFromSnapshots(typeSnapshots);

            var sharedSourceFiles = GetKnownSharedSourceFiles(cache);
            var filesToProcess = DetermineSharedFilesToProcess(cache, sharedSourceFiles, dirtyAssets, deletedAssets, fullRebuild);

            foreach (var assetPath in sharedSourceFiles.Except(filesToProcess, StringComparer.OrdinalIgnoreCase))
            {
                if (!cache.FileOutputs.TryGetValue(assetPath, out var cachedOutputs))
                    continue;

                foreach (var output in cachedOutputs)
                    keepOutputs.Add(output);
            }

            foreach (var assetPath in filesToProcess)
            {
                if (!TryReadAssetText(assetPath, out var text))
                {
                    RemoveSourceFile(cache, assetPath);
                    continue;
                }

                parsedFiles++;
                UpdateSourceSnapshotCache(cache, assetPath, text);

                var bindings = SharedTextFinder.FindBindings(text);

                if (bindings.Count == 0 && text.Contains("[Shared", StringComparison.Ordinal))
                    Debug.Log($"[PlayServ] Found [Shared] text but parsed 0 bindings in: {assetPath}");

                foundBindings += bindings.Count;

                var outs = new List<string>();

                foreach (var b in bindings)
                {
                    var declaredDto = ShortTypeName(b.DeclaredDtoTypeName);

                    var dtoName =
                        !string.IsNullOrWhiteSpace(declaredDto) ? declaredDto :
                        !string.IsNullOrWhiteSpace(b.GeneratedName) ? b.GeneratedName :
                        $"{b.OwnerTypeName}_{b.MemberName}";

                    var outFile = $"{OutputDir}/{dtoName}.g.cs".Replace("\\", "/");
                    outs.Add(outFile);
                    keepOutputs.Add(outFile);

                    var content = DtoEmitter.EmitDto(
                        ns: "Playserv.Shared",
                        dtoName: dtoName,
                        source: $"{b.OwnerTypeName}.{b.MemberName}",
                        selection: b.Selection,
                        rootTypeExpr: b.RootTypeExpr,
                        key: b.Key,
                        typeIndex: typeIndex);

                    var outHash = HashUtil.Sha256Hex(content);

                    if (cache.OutputHashes.TryGetValue(outFile, out var prevOutHash) && prevOutHash == outHash)
                        continue;

                    File.WriteAllText(outFile, content, Encoding.UTF8);
                    cache.OutputHashes[outFile] = outHash;
                    written++;
                }

                cache.FileOutputs[assetPath] = outs;
                cache.FileSharedMarkers[assetPath] = text.Contains("[Shared", StringComparison.Ordinal) ? "1" : "0";
            }

            written += DeleteStaleOutputs(cache, keepOutputs);

            SimpleCache.Save(CacheFile, cache);

            if (written > 0)
                AssetDatabase.Refresh();

            Debug.Log($"[PlayServ] {reason}: wrote={written}, parsed={parsedFiles}, bindings={foundBindings}");
        }

        private static bool ShouldRun(string reason, HashSet<string> dirtyAssets, HashSet<string> deletedAssets)
        {
            if (ShouldForceFullRebuild(reason))
                return true;

            return dirtyAssets.Count > 0 || deletedAssets.Count > 0;
        }

        private static bool ShouldForceFullRebuild(string reason)
        {
            return string.Equals(reason, "menu", StringComparison.OrdinalIgnoreCase) ||
                   !File.Exists(CacheFile) ||
                   !Directory.Exists(OutputDir);
        }

        private static void MergeTrackedAssets(string key, IEnumerable<string> assets)
        {
            if (assets == null)
                return;

            var current = LoadTrackedAssets(key);
            foreach (var asset in assets)
            {
                var normalized = NormalizeAssetPath(asset);
                if (IsRelevantSourceAsset(normalized))
                    current.Add(normalized);
            }

            SaveTrackedAssets(key, current);
        }

        private static HashSet<string> ConsumeTrackedAssets(string key)
        {
            var result = LoadTrackedAssets(key);
            SessionState.EraseString(key);
            return result;
        }

        private static HashSet<string> LoadTrackedAssets(string key)
        {
            var raw = SessionState.GetString(key, string.Empty);
            return new HashSet<string>(
                raw.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(NormalizeAssetPath)
                    .Where(IsRelevantSourceAsset),
                StringComparer.OrdinalIgnoreCase);
        }

        private static void SaveTrackedAssets(string key, HashSet<string> assets)
        {
            SessionState.SetString(key, string.Join("\n", assets.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)));
        }

        private static IEnumerable<string> EnumerateRelevantSourceAssetPaths()
        {
            return Directory.GetFiles(Application.dataPath, "*.cs", SearchOption.AllDirectories)
                .Select(ToAssetPath)
                .Where(IsRelevantSourceAsset)
                .ToArray();
        }

        private static bool IsRelevantSourceAsset(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                return false;

            var normalized = NormalizeAssetPath(assetPath);
            if (!normalized.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                return false;

            return normalized.IndexOf("/Shared/Generated/DTOs/", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private static string NormalizeAssetPath(string assetPath)
        {
            return (assetPath ?? string.Empty).Replace("\\", "/").Trim();
        }

        private static string ToAssetPath(string absolutePath)
        {
            return "Assets" + absolutePath.Substring(Application.dataPath.Length).Replace("\\", "/");
        }

        private static bool TryReadAssetText(string assetPath, out string text)
        {
            text = string.Empty;
            var fullPath = Path.GetFullPath(assetPath);
            if (!File.Exists(fullPath))
                return false;

            text = File.ReadAllText(fullPath);
            return true;
        }

        private static void UpdateSourceSnapshotCache(SimpleCache cache, string assetPath, string text)
        {
            cache.FileHashes[assetPath] = HashUtil.Sha256Hex(text);
            cache.FileTypeSnapshots[assetPath] =
                TypeIndexBuilder.SerializeSnapshot(TypeIndexBuilder.ParseSnapshot(text));
            cache.FileSharedMarkers[assetPath] = text.Contains("[Shared", StringComparison.Ordinal) ? "1" : "0";
        }

        private static void RemoveSourceFile(SimpleCache cache, string assetPath)
        {
            cache.FileHashes.Remove(assetPath);
            cache.FileTypeSnapshots.Remove(assetPath);
            cache.FileSharedMarkers.Remove(assetPath);
            cache.FileOutputs.Remove(assetPath);
        }

        private static HashSet<string> GetKnownSharedSourceFiles(SimpleCache cache)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var kv in cache.FileSharedMarkers)
            {
                if (string.Equals(kv.Value, "1", StringComparison.Ordinal))
                    result.Add(kv.Key);
            }

            foreach (var kv in cache.FileOutputs)
            {
                if (kv.Value != null && kv.Value.Count > 0)
                    result.Add(kv.Key);
            }

            return result;
        }

        private static HashSet<string> DetermineSharedFilesToProcess(
            SimpleCache cache,
            HashSet<string> sharedSourceFiles,
            HashSet<string> dirtyAssets,
            HashSet<string> deletedAssets,
            bool fullRebuild)
        {
            if (fullRebuild || deletedAssets.Count > 0)
                return new HashSet<string>(sharedSourceFiles, StringComparer.OrdinalIgnoreCase);

            var dirtySharedFiles = new HashSet<string>(
                dirtyAssets.Where(assetPath =>
                    sharedSourceFiles.Contains(assetPath) ||
                    (cache.FileSharedMarkers.TryGetValue(assetPath, out var marker) &&
                     string.Equals(marker, "1", StringComparison.Ordinal))),
                StringComparer.OrdinalIgnoreCase);

            if (dirtyAssets.Count == 0)
                return dirtySharedFiles;

            var onlySharedFilesChanged = dirtyAssets.All(assetPath =>
                sharedSourceFiles.Contains(assetPath) ||
                (cache.FileSharedMarkers.TryGetValue(assetPath, out var marker) &&
                 string.Equals(marker, "1", StringComparison.Ordinal)));

            return onlySharedFilesChanged
                ? dirtySharedFiles
                : new HashSet<string>(sharedSourceFiles.Union(dirtySharedFiles), StringComparer.OrdinalIgnoreCase);
        }

        private static void EnsureIsExternalInit(string outputDir, SimpleCache cache, HashSet<string> keepOutputs)
        {
            // This file must be compiled into the same assembly as generated DTOs.
            var outFile = $"{outputDir}/{IsExternalInitFileName}".Replace("\\", "/");
            keepOutputs.Add(outFile);

            var content = BuildIsExternalInitContent();
            var outHash = HashUtil.Sha256Hex(content);

            // IMPORTANT: cache can say "up-to-date" even if the file was deleted.
            // Always regenerate if the file is missing.
            if (File.Exists(outFile) &&
                cache.OutputHashes.TryGetValue(outFile, out var prevOutHash) &&
                prevOutHash == outHash)
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(outputDir);
                File.WriteAllText(outFile, content, Encoding.UTF8);
                cache.OutputHashes[outFile] = outHash;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PlayServ] Failed to write {IsExternalInitFileName}: {e.Message}");
            }
        }

        private static string BuildIsExternalInitContent()
        {
            return
@"// <auto-generated />
// Unity 2021.3 compatibility: enables C# 9 init-only setters (System.Runtime.CompilerServices.IsExternalInit)
namespace System.Runtime.CompilerServices
{
    public sealed class IsExternalInit { }
}
";
        }

        private static string ShortTypeName(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return "";

            s = s.Trim();

            // remove nullable
            if (s.EndsWith("?", StringComparison.Ordinal))
                s = s.Substring(0, s.Length - 1);

            // remove array
            if (s.EndsWith("[]", StringComparison.Ordinal))
                s = s.Substring(0, s.Length - 2);

            // remove generic outer if someone declared like List<Dto> (not your case, but safe)
            var lt = s.IndexOf('<');
            if (lt > 0)
                s = s.Substring(0, lt).Trim();

            // take last segment after dot
            var lastDot = s.LastIndexOf('.');
            if (lastDot >= 0 && lastDot + 1 < s.Length)
                s = s.Substring(lastDot + 1);

            return s.Trim();
        }

        private static int DeleteStaleOutputs(SimpleCache cache, HashSet<string> keep)
        {
            int deleted = 0;

            foreach (var outFile in cache.OutputHashes.Keys.ToArray())
            {
                if (keep.Contains(outFile))
                    continue;

                try
                {
                    if (File.Exists(outFile))
                    {
                        File.Delete(outFile);
                        deleted++;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[PlayServ] Failed to delete stale output '{outFile}': {e.Message}");
                }

                cache.OutputHashes.Remove(outFile);
            }

            return deleted;
        }
    }
}

#endif
#nullable restore
