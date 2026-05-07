using System;
using System.Collections.Generic;
using UnityEditor;

namespace Playserv.CodeGenerator.Editor
{
    internal sealed class SharedCodeGeneratorAssetPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            var dirtyAssets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var removedAssets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            AddRelevantAssets(importedAssets, dirtyAssets);
            AddRelevantAssets(movedAssets, dirtyAssets);
            AddRelevantAssets(deletedAssets, removedAssets);
            AddRelevantAssets(movedFromAssetPaths, removedAssets);

            if (dirtyAssets.Count == 0 && removedAssets.Count == 0)
                return;

            SharedCodeGenerator.TrackAssetChanges(dirtyAssets, removedAssets, "assetPostprocessor");
        }

        private static void AddRelevantAssets(IEnumerable<string> assets, HashSet<string> target)
        {
            if (assets == null)
                return;

            foreach (var asset in assets)
            {
                if (string.IsNullOrWhiteSpace(asset) ||
                    !asset.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                target.Add(asset.Replace("\\", "/"));
            }
        }
    }
}
