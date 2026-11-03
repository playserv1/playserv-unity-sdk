using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using UnityEngine;

namespace Playserv.Editor.Distribution
{
    // Independent of SDK assemblies; the separate preparation tool handles the
    // first upgrade before replacing source assemblies can block compilation.
    [InitializeOnLoad]
    public sealed class PlayServBinaryUpgrade : AssetPostprocessor
    {
        private static bool _running;
        private static bool _queued;

        static PlayServBinaryUpgrade()
        {
            var manifest = ReadManifest();
            if (manifest != null && Directory.Exists("Assets"))
                foreach (var file in Directory.GetFiles("Assets", "*.asmdef", SearchOption.AllDirectories))
                    MigrateFile(file.Replace('\\', '/'), manifest);
            QueueMigration();
        }

        private static JObject ReadManifest()
        {
            var package = PackageInfo.FindForAssembly(typeof(PlayServBinaryUpgrade).Assembly);
            var root = package?.resolvedPath ?? Path.GetDirectoryName(Path.GetDirectoryName(typeof(PlayServBinaryUpgrade).Assembly.Location));
            var file = Path.Combine(root, "Runtime/PlayServBinaryDistribution.json");
            return File.Exists(file) ? JObject.Parse(File.ReadAllText(file)) : null;
        }

        private void OnPreprocessAsset()
        {
            if (_running || !assetPath.StartsWith("Assets/", StringComparison.Ordinal)) return;
            var manifest = ReadManifest();
            if (manifest != null) MigrateFile(assetPath, manifest);
        }

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (!_running) QueueMigration();
        }

        private static void QueueMigration()
        {
            if (_queued) return;
            _queued = true;
            EditorApplication.delayCall += MigrateProject;
        }

        public static void MigrateProject()
        {
            _queued = false;
            if (_running) return;
            var manifest = ReadManifest();
            if (manifest == null || !Directory.Exists("Assets")) return;
            _running = true;
            try
            {
                var changed = false;
                foreach (var file in Directory.GetFiles("Assets", "*", SearchOption.AllDirectories))
                    changed |= MigrateFile(file.Replace('\\', '/'), manifest);
                if (changed) AssetDatabase.Refresh();
            }
            finally { _running = false; }
        }

        private static bool MigrateFile(string file, JObject manifest)
        {
            if (!File.Exists(file) || file.EndsWith(".meta", StringComparison.Ordinal)) return false;
            var extension = Path.GetExtension(file);
            if (extension != ".asmdef" && extension != ".asset" && extension != ".prefab" &&
                extension != ".unity" && extension != ".controller" && extension != ".overrideController" &&
                extension != ".anim" && extension != ".playable") return false;
            var bytes = File.ReadAllBytes(file);
            if (bytes.Contains((byte)0)) return false;
            var text = Encoding.UTF8.GetString(bytes);
            var updated = extension == ".asmdef" ? RewriteAssembly(text, manifest) : RewriteScriptReferences(text, manifest);
            if (updated == text) return false;
            var backup = Path.Combine("Library/PlayServ/BinaryUpgrade", file);
            Directory.CreateDirectory(Path.GetDirectoryName(backup));
            if (!File.Exists(backup)) File.WriteAllBytes(backup, bytes);
            var temporary = file + ".playserv-upgrade";
            File.WriteAllText(temporary, updated, new UTF8Encoding(false));
            File.Replace(temporary, file, null);
            return true;
        }

        public static string RewriteAssembly(string text, JObject manifest, IEnumerable<string> automaticPlugins = null)
        {
            var definition = JObject.Parse(text);
            var references = (definition["references"] as JArray)?.Values<string>().ToArray() ?? Array.Empty<string>();
            var matched = ((JArray)manifest["assemblies"]).OfType<JObject>()
                .Where(a => references.Contains((string)a["name"]) || references.Contains("GUID:" + (string)a["guid"]))
                .ToArray();
            if (matched.Length == 0) return text;
            var names = matched.Select(a => (string)a["name"] + ".dll").ToArray();
            definition["references"] = new JArray(references.Where(reference => !matched.Any(a =>
                reference == (string)a["name"] || reference == "GUID:" + (string)a["guid"])));
            var isTest = (definition["optionalUnityReferences"] as JArray)?.Values<string>().Contains("TestAssemblies") == true;
            var explicitReferences = (bool?)definition["overrideReferences"] == true;
            if (!explicitReferences && !isTest) return definition.ToString() + "\n";
            var existing = (definition["precompiledReferences"] as JArray)?.Values<string>().ToArray() ?? Array.Empty<string>();
            var automatic = explicitReferences ? Array.Empty<string>() : automaticPlugins ?? AutomaticPlugins((string)definition["name"]);
            var wanted = existing.Concat(names).Concat(automatic).Concat(isTest ? new[] { "nunit.framework.dll", "Newtonsoft.Json.dll" } : Array.Empty<string>())
                .Distinct().OrderBy(value => value, StringComparer.Ordinal).ToArray();
            definition["overrideReferences"] = true;
            definition["precompiledReferences"] = new JArray(wanted);
            return definition.ToString() + "\n";
        }

        private static IEnumerable<string> AutomaticPlugins(string assemblyName)
        {
            var plugins = new HashSet<string>(PluginImporter.GetAllImporters().Where(importer => !importer.isNativePlugin)
                .Select(importer => Path.GetFileName(importer.assetPath)), StringComparer.Ordinal);
            var assembly = CompilationPipeline.GetAssemblies(AssembliesType.Editor).FirstOrDefault(candidate => candidate.name == assemblyName);
            return (assembly?.compiledAssemblyReferences ?? Array.Empty<string>()).Select(Path.GetFileName).Where(plugins.Contains).Distinct();
        }

        public static string RewriteScriptReferences(string text, JObject manifest)
        {
            if (!text.TrimStart('\uFEFF').StartsWith("%YAML", StringComparison.Ordinal)) return text;
            foreach (var script in ((JArray)manifest["scripts"]).OfType<JObject>())
            {
                var oldGuid = (string)script["guid"];
                var newGuid = (string)script["targetGuid"];
                var fileId = (long)script["fileId"];
                text = Regex.Replace(text, @"\{fileID:\s*11500000,\s*guid:\s*" + Regex.Escape(oldGuid) + @",\s*type:\s*3\}",
                    "{fileID: " + fileId + ", guid: " + newGuid + ", type: 3}");
            }
            return text;
        }
    }
}
