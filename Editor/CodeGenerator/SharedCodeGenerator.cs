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

            SessionState.SetBool(RunningKey, true);
            try
            {
                GenerateAll(reason);
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
                GenerateAll(reason);
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

        private static void GenerateAll(string reason)
        {
            Directory.CreateDirectory(OutputDir);
            Directory.CreateDirectory(Path.GetDirectoryName(CacheFile) ?? CacheFolder);

            var cache = SimpleCache.Load(CacheFile);

            // Keep outputs so stale cleanup doesn't delete them
            var keepOutputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Ensure IsExternalInit exists for Unity 2021.3 (init setters)
            EnsureIsExternalInit(OutputDir, cache, keepOutputs);

            var csFilesAbs = Directory.GetFiles(Application.dataPath, "*.cs", SearchOption.AllDirectories)
                .Select(p => p.Replace("\\", "/"))
                .Where(p => !p.Contains("/Shared/Generated/DTOs/", StringComparison.OrdinalIgnoreCase))
                .Where(p => !p.EndsWith("/Editor/SharedCodeGenerator.cs", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            // Build a naive type index from all sources (needed to resolve DTO field types).
            var typeIndex = TypeIndexBuilder.BuildFromAllCsFiles(csFilesAbs);

            int parsedFiles = 0;
            int foundBindings = 0;
            int written = 0;

            foreach (var abs in csFilesAbs)
            {
                var rel = "Assets" + abs.Substring(Application.dataPath.Length).Replace("\\", "/");
                var text = File.ReadAllText(abs);
                parsedFiles++;

                var fileHash = HashUtil.Sha256Hex(text);

                // If unchanged, keep previously known outputs for stale cleanup.
                if (cache.FileHashes.TryGetValue(rel, out var old) && old == fileHash)
                {
                    if (cache.FileOutputs.TryGetValue(rel, out var outsOld))
                    {
                        foreach (var o in outsOld)
                            keepOutputs.Add(o);
                    }
                    continue;
                }

                var bindings = SharedTextFinder.FindBindings(text);

                if (bindings.Count == 0 && text.Contains("[Shared", StringComparison.Ordinal))
                    Debug.Log($"[PlayServ] Found [Shared] text but parsed 0 bindings in: {rel}");

                foundBindings += bindings.Count;

                var outs = new List<string>();

                foreach (var b in bindings)
                {
                    // 1) If member has explicit declared DTO type (field/property type) -> use it
                    // 2) Else use GeneratedName
                    // 3) Else fallback to Owner_Member
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

                cache.FileHashes[rel] = fileHash;
                cache.FileOutputs[rel] = outs;
            }

            written += DeleteStaleOutputs(cache, keepOutputs);

            SimpleCache.Save(CacheFile, cache);

            if (written > 0)
                AssetDatabase.Refresh();

            Debug.Log($"[PlayServ] {reason}: wrote={written}, parsed={parsedFiles}, bindings={foundBindings}");
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