// Assets/Playserv/Editor/Events/EventsCodeGeneratorPostprocessor.cs
#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;

namespace Playserv.Events.Editor
{
    internal sealed class EventsCodeGeneratorPostprocessor : AssetPostprocessor
    {
        private static bool _isGenerating;

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (_isGenerating)
                return;

            if (importedAssets == null || importedAssets.Length == 0)
                return;

            var hasScriptChanges = importedAssets.Any(a =>
                a.EndsWith(".cs", StringComparison.OrdinalIgnoreCase));

            if (!hasScriptChanges)
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
    }
}
#endif