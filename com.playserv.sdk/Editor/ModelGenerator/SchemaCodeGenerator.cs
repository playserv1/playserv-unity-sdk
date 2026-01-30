using UnityEditor;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.IO;
using System.Linq;
using UnityEngine;
using Playserv.CodeGenerator;

namespace Playserv.ModelGenerator.Editor
{
    internal static class SchemaCodeGenerator
    {
        private const string RootFolderPath = "/Shared/Generated/Models";
        private const string SchemaFilePath = "Assets/Resources/schema.json";
        
        // [MenuItem("Tools/PlayServ/Generate Models from JSON Schema")]
        public static void Generate()
        {
            foreach (var file in GetFilesDataCollection())
            {
                GenerateFileClass(file.Key, file.Value);
            }
            AssetDatabase.Refresh();
        }
        
        public static void GenerateModels()
        {
            foreach (var file in GetFilesDataCollection(false))
            {
                GenerateFileClass(file.Key, file.Value);
            }
            AssetDatabase.Refresh();
        }

        private static Dictionary<string,string> GetFilesDataCollection(bool selectFile = true)
        {
            // Parameters: Title, Directory to start in, Extension (empty string for all)
            var path = selectFile ? EditorUtility.OpenFilePanel("Select json schema", "", "json") : SchemaFilePath;
            
            if (!string.IsNullOrEmpty(path))
            {
                Debug.Log("Selected json schema File Path: " + path);
            
                string content = File.ReadAllText(path);
                
                try
                {
                    // 1. Setup Deserialization
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        ReadCommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true
                    };

                    // 2. Deserialize Schema
                    JsonSchema schema = JsonSerializer.Deserialize<JsonSchema>(content, options)!;
                    Debug.Log($"[LOG] Schema Version: {schema.XVersion}");
                    Debug.Log($"[LOG] Timestamp: {schema.XTimestamp}");
                    Debug.Log($"[LOG] Definitions Found: {SchemaUtils.GetAllDefinitions(schema).Count()}");
                    Debug.Log("");

                    // 3. Generate Code
                    var generator = new DotNetGenerator();
                    Dictionary<string,string> generatedCode = generator.Generate(schema);

                    // 4. Print Result
                    Debug.Log("--- GENERATED C# CLASSES ---");
                    foreach (var file in generatedCode)
                    {
                        Debug.Log($"// FileName: {file.Key}");
                        Debug.Log(file.Value);
                        Debug.Log(Environment.NewLine);
                    }
                    Debug.Log( "-----------------------------");

                    Debug.Log("\nGeneration complete.");
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
    }
}