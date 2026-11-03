using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Playserv.Editor
{
    internal static class PlayServBinaryPackage
    {
        internal const string ManifestPath = "Runtime/PlayServBinaryDistribution.json";

        internal static bool IsInstalled
        {
            get
            {
                return PlayServPackagePathResolver.TryResolveRootForScript(nameof(PlayServBinaryPackage),
                    "/Editor/Modules/PlayServBinaryPackage.cs", out var root) &&
                    File.Exists(root.ToAbsolutePath(ManifestPath));
            }
        }

        internal static Manifest Read(PlayServPackageRoot root)
        {
            var path = root.ToAbsolutePath(ManifestPath);
            if (!File.Exists(path)) return null;
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(path));
            if (manifest == null || manifest.schemaVersion != 1 || manifest.binaries == null)
                throw new InvalidDataException("Unsupported PlayServ binary package manifest.");
            return manifest;
        }

        [Serializable]
        internal sealed class Manifest
        {
            public int schemaVersion;
            public Binary[] binaries;

            internal bool Contains(string assembly) => binaries.Any(binary => binary.assembly == assembly);

            internal bool HasConstraint(PlayServPackageRoot root, string assembly, string constraint)
            {
                var variants = binaries.Where(binary => binary.assembly == assembly).ToArray();
                return variants.Length > 0 && variants.All(binary =>
                {
                    var absolute = root.ToAbsolutePath(binary.path);
                    if (!File.Exists(absolute)) return false;
                    var importer = AssetImporter.GetAtPath(root.ToAssetPath(absolute)) as PluginImporter;
                    return importer != null && importer.DefineConstraints.Contains(constraint);
                });
            }
        }

        [Serializable]
        internal sealed class Binary
        {
            public string path;
            public string assembly;
        }
    }
}
