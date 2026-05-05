// Assets/Playserv/Editor/Events/EventsCodeGeneratorPostprocessor.cs
#if UNITY_EDITOR && !PLAYSERV_DISABLE_EVENTS
using System;
using System.Linq;
using UnityEditor;

namespace Playserv.Events.Editor
{
    internal sealed class EventsCodeGeneratorPostprocessor : AssetPostprocessor
    {
        private static bool _isGenerating;
        private static readonly string[] EventRelevantPathMarkers =
        {
            "/Editor/Events/",
            "/Runtime/Modules/Events/"
        };
        private static readonly string GeneratedEventsMarker = NormalizePath(EventsCodeGenerator.GeneratedEventsDirectoryPath);

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (_isGenerating)
                return;

            if (!IsEventsModuleEnabled())
                return;

            if (!ShouldGenerate(importedAssets, deletedAssets, movedAssets, movedFromAssetPaths))
                return;

            _isGenerating = true;
            try
            {
                EventsCodeGenerator.Generate();
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"EventsCodeGenerator failed: {ex.Message}");
            }
            finally
            {
                _isGenerating = false;
            }
        }

        private static bool ShouldGenerate(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (HasRelevantImportedSource(importedAssets))
                return true;

            if (HasPotentiallyRelevantDeletedOrMovedSource(deletedAssets) ||
                HasPotentiallyRelevantDeletedOrMovedSource(movedAssets) ||
                HasPotentiallyRelevantDeletedOrMovedSource(movedFromAssetPaths))
            {
                return true;
            }

            return false;
        }

        private static bool HasRelevantImportedSource(string[] assetPaths)
        {
            if (assetPaths == null || assetPaths.Length == 0)
                return false;

            foreach (var assetPath in assetPaths)
            {
                if (!IsCSharpSource(assetPath) || IsGeneratedEventsAsset(assetPath))
                    continue;

                if (MatchesEventPath(assetPath))
                    return true;

                if (!System.IO.File.Exists(assetPath))
                    continue;

                try
                {
                    var text = System.IO.File.ReadAllText(assetPath);
                    if (text.IndexOf("[Event", StringComparison.Ordinal) >= 0 ||
                        text.IndexOf("EventAttribute", StringComparison.Ordinal) >= 0)
                    {
                        return true;
                    }
                }
                catch
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasPotentiallyRelevantDeletedOrMovedSource(string[] assetPaths)
        {
            if (assetPaths == null || assetPaths.Length == 0)
                return false;

            return assetPaths.Any(assetPath =>
                IsCSharpSource(assetPath) &&
                !IsGeneratedEventsAsset(assetPath));
        }

        private static bool IsCSharpSource(string assetPath)
        {
            return !string.IsNullOrWhiteSpace(assetPath) &&
                   assetPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsGeneratedEventsAsset(string assetPath)
        {
            return !string.IsNullOrWhiteSpace(assetPath) &&
                   NormalizePath(assetPath).IndexOf(GeneratedEventsMarker, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool MatchesEventPath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                return false;

            var normalized = NormalizePath(assetPath);
            return EventRelevantPathMarkers.Any(marker =>
                normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string NormalizePath(string assetPath)
        {
            return string.IsNullOrWhiteSpace(assetPath)
                ? string.Empty
                : assetPath.Replace('\\', '/');
        }

        private static bool IsEventsModuleEnabled()
        {
            var defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
            return defines.IndexOf(Playserv.Editor.Const.DefineDisableEvents, StringComparison.Ordinal) < 0;
        }
    }
}
#endif
