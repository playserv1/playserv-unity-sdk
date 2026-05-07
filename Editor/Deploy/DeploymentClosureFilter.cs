using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Playserv.Deploy.Editor.Analysis;
using UnityEditor;
using UnityEngine;

namespace Playserv.Deploy.Editor
{
    internal sealed class DeploymentClosureFilter
    {
        public List<string> CollectDeployFiles(
            DefaultAsset deployFolder,
            bool includeSubfolders,
            string deployPattern,
            out string error)
        {
            error = null;

            if (deployFolder == null)
            {
                error = "Please select a Folder to deploy.";
                return new List<string>();
            }

            var folderPath = AssetDatabase.GetAssetPath(deployFolder);
            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                error = "Selected asset is not a folder.";
                return new List<string>();
            }

            var absoluteFolderPath = Path.GetFullPath(folderPath);
            var option = includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var pattern = string.IsNullOrWhiteSpace(deployPattern) ? "*" : deployPattern.Trim();

            string[] files;
            try
            {
                files = Directory.GetFiles(absoluteFolderPath, pattern, option);
            }
            catch (Exception e)
            {
                error = $"Failed to list files: {e.Message}";
                return new List<string>();
            }

            var candidateFiles = files
                .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            AnalysisResult analysisResult;
            try
            {
                var analyzer = new FunctionAnalyzerService();
                analysisResult = analyzer.AnalyzeFiles(candidateFiles);
            }
            catch (Exception ex)
            {
                error = $"Failed to analyze files: {ex.Message}";
                return new List<string>();
            }

            if (!analysisResult.Success)
            {
                var distinctErrors = analysisResult.Errors
                    .Where(e => !string.IsNullOrWhiteSpace(e))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                var errorsToShow = distinctErrors.Take(20).ToList();
                var hiddenErrorsCount = Math.Max(0, distinctErrors.Count - errorsToShow.Count);

                var details = errorsToShow.Count == 0
                    ? "  - Unknown validation error."
                    : string.Join(Environment.NewLine, errorsToShow.Select(e => $"  - {e}"));

                if (hiddenErrorsCount > 0)
                    details += Environment.NewLine + $"  - ...and {hiddenErrorsCount} more";

                error = "Code analysis failed:" + Environment.NewLine + details;
                return new List<string>();
            }

            var analyzedFileSet = new HashSet<string>(
                analysisResult.FilesToCompile.Where(f => !string.IsNullOrWhiteSpace(f)),
                StringComparer.OrdinalIgnoreCase);

            var result = candidateFiles
                .Where(analyzedFileSet.Contains)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var excludedByAnalysis = candidateFiles
                .Where(f => !analyzedFileSet.Contains(f))
                .ToList();

            if (excludedByAnalysis.Count > 0)
            {
                Debug.LogWarning(
                    $"[PlayServ] Excluded {excludedByAnalysis.Count} file(s) not required by analyzer RPC dependency closure: " +
                    string.Join(", ", excludedByAnalysis.Take(15)));
            }

            if (result.Count == 0)
                error = "No files matched the current pattern.";
            else
                Debug.Log($"[PlayServ] Final deploy file count: {result.Count}. Files: {string.Join(", ", result)}");

            return result;
        }
    }
}
