using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Playserv.CodeGenerator;
using Playserv.Editor;
using Playserv.Modules;
using UnityEditor;
using UnityEngine;

namespace Playserv.ModelGenerator.Editor
{
    internal sealed class PlayServModelGenerationResult
    {
        private PlayServModelGenerationResult(
            bool success,
            int generatedFileCount,
            string sourcePath,
            string error)
        {
            Success = success;
            GeneratedFileCount = generatedFileCount;
            SourcePath = sourcePath ?? string.Empty;
            Error = error ?? string.Empty;
        }

        public bool Success { get; }

        public int GeneratedFileCount { get; }

        public string SourcePath { get; }

        public string Error { get; }

        public static PlayServModelGenerationResult Completed(
            int generatedFileCount,
            string sourcePath)
        {
            return new PlayServModelGenerationResult(
                true,
                generatedFileCount,
                sourcePath,
                string.Empty);
        }

        public static PlayServModelGenerationResult Failed(
            string sourcePath,
            string error)
        {
            return new PlayServModelGenerationResult(false, 0, sourcePath, error);
        }
    }

    internal static class SchemaCodeGenerator
    {
        public static void Generate()
        {
            var path = EditorUtility.OpenFilePanel(
                "Select JSON schema",
                string.Empty,
                "json");
            if (string.IsNullOrWhiteSpace(path))
            {
                Debug.LogWarning("Schema file selection was cancelled.");
                return;
            }

            Report(GenerateFromSchemaFile(path, acceptAsCurrent: true));
        }

        public static void GenerateModels(bool isLatestSchemaUse = true)
        {
            Report(GenerateModelsWithResult(isLatestSchemaUse));
        }

        internal static PlayServModelGenerationResult GenerateModelsWithResult(
            bool useLatestSchema)
        {
            var sourcePath = useLatestSchema
                ? PlayServServerSchemaWorkflow.LatestSchemaAssetPath
                : PlayServServerSchemaWorkflow.CurrentSchemaAssetPath;
            return GenerateFromSchemaFile(
                PlayServServerSchemaWorkflow.ToAbsolutePath(sourcePath),
                acceptAsCurrent: useLatestSchema,
                sourcePath);
        }

        internal static PlayServModelGenerationResult GenerateFromSchemaFile(
            string absoluteSchemaPath,
            bool acceptAsCurrent,
            string displayPath = null)
        {
            var sourcePath = string.IsNullOrWhiteSpace(displayPath)
                ? absoluteSchemaPath
                : displayPath;
            if (string.IsNullOrWhiteSpace(absoluteSchemaPath) ||
                !File.Exists(absoluteSchemaPath))
            {
                return PlayServModelGenerationResult.Failed(
                    sourcePath,
                    $"Schema file was not found: {sourcePath}");
            }

            try
            {
                var content = File.ReadAllText(absoluteSchemaPath);
                var root = SchemaJsonReader.ReadRoot(content);
                if (!SchemaJsonReader.IsSupportedRoot(root))
                {
                    return PlayServModelGenerationResult.Failed(
                        sourcePath,
                        $"{sourcePath} does not contain a supported PlayServ schema.");
                }

                var generator = new DotNetGenerator();
                var generatedCode = generator.Generate(root.JsonSchema);
                if (generatedCode == null)
                {
                    return PlayServModelGenerationResult.Failed(
                        sourcePath,
                        "The schema code generator returned no result.");
                }

                if (!TryPrepareGeneratedFiles(
                        generatedCode,
                        out var generatedFiles,
                        out var validationError))
                {
                    return PlayServModelGenerationResult.Failed(
                        sourcePath,
                        validationError);
                }

                WriteGeneratedFiles(generatedFiles);
                if (acceptAsCurrent)
                {
                    WriteText(
                        PlayServServerSchemaWorkflow.ToAbsolutePath(
                            PlayServServerSchemaWorkflow.CurrentSchemaAssetPath),
                        content);
                }

                SaveCurrentSchemaInfo(
                    root.JsonSchema.XVersion,
                    root.JsonSchema.XTimestamp);
                AssetDatabase.Refresh();
                ResetSchemaSelectionProviderIfAvailable();

                Debug.Log(
                    $"[PlayServ Schema] Generated {generatedFiles.Count} C# model files " +
                    $"from {sourcePath}. Definitions: " +
                    $"{SchemaUtils.GetAllDefinitions(root.JsonSchema).Count()}.");
                return PlayServModelGenerationResult.Completed(
                    generatedFiles.Count,
                    sourcePath);
            }
            catch (Exception exception)
            {
                return PlayServModelGenerationResult.Failed(
                    sourcePath,
                    exception.GetBaseException().Message);
            }
        }

        public static void CheckNewVersionJsonSchema()
        {
            var latest = PlayServServerSchemaWorkflow.ReadLatest(out var error);
            if (!latest.Exists)
            {
                Debug.LogWarning(
                    $"[PlayServ Schema] Latest schema was not found at " +
                    $"{PlayServServerSchemaWorkflow.LatestSchemaAssetPath}.");
                return;
            }

            if (!string.IsNullOrWhiteSpace(error))
            {
                Debug.LogError($"[PlayServ Schema] {error}");
                return;
            }

            EditorPrefs.SetString(
                Const.PrefKeyJsonSchemaLatestTimestamp,
                latest.Timestamp);
            EditorPrefs.SetString(
                Const.PrefKeyJsonSchemaLatestVersion,
                latest.Version);
        }

        internal static bool TryGetGeneratedFileName(
            string className,
            out string fileName)
        {
            fileName = string.Empty;
            var value = className?.Trim();
            if (string.IsNullOrWhiteSpace(value) ||
                value == "." ||
                value == ".." ||
                value.IndexOfAny(new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' }) >= 0 ||
                value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                return false;
            }

            fileName = value.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                ? value
                : value + ".cs";
            return true;
        }

        private static bool TryPrepareGeneratedFiles(
            IReadOnlyDictionary<string, string> generatedCode,
            out Dictionary<string, string> generatedFiles,
            out string error)
        {
            generatedFiles = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            error = string.Empty;

            foreach (var entry in generatedCode)
            {
                if (!TryGetGeneratedFileName(entry.Key, out var fileName))
                {
                    error =
                        $"Generated model name '{entry.Key}' is not a safe file name.";
                    return false;
                }

                if (generatedFiles.ContainsKey(fileName))
                {
                    error =
                        $"Multiple generated models resolve to '{fileName}'.";
                    return false;
                }

                generatedFiles.Add(fileName, entry.Value ?? string.Empty);
            }

            return true;
        }

        private static void WriteGeneratedFiles(
            IReadOnlyDictionary<string, string> generatedFiles)
        {
            var outputDirectory = PlayServServerSchemaWorkflow.ToAbsolutePath(
                PlayServServerSchemaWorkflow.GeneratedModelsAssetPath);
            Directory.CreateDirectory(outputDirectory);

            var expectedPaths = new HashSet<string>(
                generatedFiles.Keys.Select(
                    fileName => Path.GetFullPath(
                        Path.Combine(outputDirectory, fileName))),
                StringComparer.OrdinalIgnoreCase);

            foreach (var entry in generatedFiles)
            {
                WriteText(
                    Path.Combine(outputDirectory, entry.Key),
                    entry.Value);
            }

            foreach (var existingFile in Directory.GetFiles(
                         outputDirectory,
                         "*.cs",
                         SearchOption.TopDirectoryOnly))
            {
                var fullPath = Path.GetFullPath(existingFile);
                if (expectedPaths.Contains(fullPath))
                    continue;

                File.Delete(fullPath);
                var metaPath = fullPath + ".meta";
                if (File.Exists(metaPath))
                    File.Delete(metaPath);
            }
        }

        private static void WriteText(string path, string content)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(path, content ?? string.Empty, new UTF8Encoding(false));
        }

        private static void SaveCurrentSchemaInfo(string version, string timestamp)
        {
            EditorPrefs.SetString(
                Const.PrefKeyJsonSchemaVersion,
                version ?? string.Empty);
            EditorPrefs.SetString(
                Const.PrefKeyJsonSchemaTimestamp,
                timestamp ?? string.Empty);
        }

        private static void Report(PlayServModelGenerationResult result)
        {
            if (result.Success)
            {
                Debug.Log(
                    $"[PlayServ Schema] Generated {result.GeneratedFileCount} models " +
                    $"from {result.SourcePath}.");
                return;
            }

            Debug.LogError($"[PlayServ Schema] {result.Error}");
        }

        private static void ResetSchemaSelectionProviderIfAvailable()
        {
            if (!PlayServEditorModuleAvailability.IsRuntimeModuleAvailable(
                    PlayServModuleManifest.DataSubscriptionId))
            {
                return;
            }

            PlayServSchemaSelectionRegistry.TryReset();
        }
    }
}
