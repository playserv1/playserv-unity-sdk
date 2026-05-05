using UnityEditor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Playserv.CodeGenerator;
using Playserv.DataSubscription;
using Playserv.Editor;

namespace Playserv.ModelGenerator.Editor
{
    internal static class SchemaCodeGenerator
    {
        private const string RootFolderPath = "/Shared/Generated/Models";
        private const string LatestSchemaFilePath = "Assets/Resources/latest-schema.json";
        private const string CurrentSchemaFilePath = "Assets/Resources/current-schema.json";

        // [MenuItem("Tools/PlayServ/Generate Models from JSON Schema")]
        public static void Generate()
        {
            foreach (var file in GetFilesDataCollection())
            {
                GenerateFileClass(file.Key, file.Value);
            }

            AssetDatabase.Refresh();
            SchemaSelectionProvider.Reset();
        }

        public static void GenerateModels(bool isLatestSchemaUse = true)
        {
            if (Directory.Exists(Application.dataPath + RootFolderPath))
                Directory.Delete(Application.dataPath + RootFolderPath, true);

            foreach (var file in GetFilesDataCollection(false, isLatestSchemaUse))
            {
                GenerateFileClass(file.Key, file.Value);
            }

            AssetDatabase.Refresh();
            SchemaSelectionProvider.Reset();
        }

        private static Dictionary<string, string> GetFilesDataCollection(bool selectFile = true,
            bool isLatestSchemaUse = true)
        {
            // Parameters: Title, Directory to start in, Extension (empty string for all)
            var path = selectFile ? EditorUtility.OpenFilePanel("Select json schema", "", "json") :
                isLatestSchemaUse ? LatestSchemaFilePath : CurrentSchemaFilePath;

            if (!string.IsNullOrEmpty(path))
            {
                Debug.Log("Selected json schema File Path: " + path);

                string content = File.ReadAllText(path);

                try
                {
                    JsonSchemaRoot root = SchemaJsonReader.ReadRoot(content);

                    EditorPrefs.SetString(Const.PrefKeyJsonSchemaTimestamp, root.JsonSchema.XTimestamp);
                    EditorPrefs.SetString(Const.PrefKeyJsonSchemaVersion, root.JsonSchema.XVersion);

                    Debug.Log($"[LOG] Schema Version: {root.JsonSchema.XVersion}");
                    Debug.Log($"[LOG] Timestamp: {root.JsonSchema.XTimestamp}");
                    Debug.Log($"[LOG] Definitions Found: {SchemaUtils.GetAllDefinitions(root.JsonSchema).Count()}");
                    Debug.Log("");

                    // 3. Generate Code
                    var generator = new DotNetGenerator();
                    Dictionary<string, string> generatedCode = generator.Generate(root.JsonSchema);

                    // 4. Print Result
                    Debug.Log("--- GENERATED C# CLASSES ---");
                    foreach (var file in generatedCode)
                    {
                        Debug.Log($"// FileName: {file.Key}");
                        Debug.Log(file.Value);
                        Debug.Log(Environment.NewLine);
                    }

                    Debug.Log("-----------------------------");

                    Debug.Log("\nGeneration complete.");

                    if (selectFile || (!selectFile && !isLatestSchemaUse))
                    {
                        File.WriteAllText(CurrentSchemaFilePath, content);
                        Debug.Log("[LOG] Current schema saved/updated.");
                    }

                    return generatedCode;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Error: {ex.Message}");
                    if (ex.InnerException != null) Debug.LogError($"Detail: {ex.InnerException.Message}");
                }
            }
            else
            {
                Debug.LogWarning("File selection was cancelled.");
            }

            return null;
        }

        private static void GenerateFileClass(string className, string content)
        {
            string folderPath = Application.dataPath + RootFolderPath;
            string fullPath = folderPath + "/" + className + ".cs";

            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            File.WriteAllText(fullPath, content);

            Debug.Log($"Successfully created C# file at: {fullPath}");
        }

        public static void CheckNewVersionJsonSchema()
        {
            if (!File.Exists(LatestSchemaFilePath))
            {
                Debug.LogWarning($"[LOG] Latest schema file not found at '{LatestSchemaFilePath}'.");
                return;
            }

            var content = File.ReadAllText(LatestSchemaFilePath);
            JsonSchemaRoot root = SchemaJsonReader.ReadRoot(content);

            EditorPrefs.SetString(Const.PrefKeyJsonSchemaLatestTimestamp, root.JsonSchema.XTimestamp);
            EditorPrefs.SetString(Const.PrefKeyJsonSchemaLatestVersion, root.JsonSchema.XVersion);

            Debug.Log($"[LOG] Checking New Schema...");
            Debug.Log($"[LOG] Schema Version: {root.JsonSchema.XVersion}");
            Debug.Log($"[LOG] Timestamp: {root.JsonSchema.XTimestamp}");
        }
    }
}
