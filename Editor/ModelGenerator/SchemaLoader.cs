using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Playserv.Editor;
using Playserv.ModelGenerator.Editor;
using Playserv.Wrapper;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

public static class SchemaLoader
{
    private const string SchemaBySdkKeyEndpointPath = "/api/schemas/by-sdk-key";
    private const string LatestSchemaFileName = "latest-schema.json";
    
    public static void LoadSchema(string token) => _ = DownloadSchema(token);

    public static void CheckNewSchema()
    {
        SchemaCodeGenerator.CheckNewVersionJsonSchema();
    }
    
    private static async Task DownloadSchema(string token)
    {
        var schemaUrl = ResolveSchemaUrl();
        await DownloadAndSaveToResourcesAsync(schemaUrl, token);
    }
    
    private static async Task DownloadAndSaveToResourcesAsync(string url, string token)
    {
        var json = await LoadJson(url, token);
        
        var resourcesDir = Path.Combine(Application.dataPath, "Resources");
        if (!Directory.Exists(resourcesDir))
            Directory.CreateDirectory(resourcesDir);

        var filePath = Path.Combine(resourcesDir, LatestSchemaFileName);
        await File.WriteAllTextAsync(filePath, json);
        
        AssetDatabase.Refresh();
        ResetSchemaSelectionProviderIfAvailable();

        Debug.Log($"[SchemaDownloader] schema.json saved to {filePath}");
    }

    private static async Task<string> LoadJson(string url, string sdkKey)
    {
        var payload = $"{{\"key\":\"{sdkKey}\"}}";
        var bodyRaw = Encoding.UTF8.GetBytes(payload);
        
        using var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);

        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        
        request.SetRequestHeader("Accept", "text/plain");
        request.SetRequestHeader("Content-Type", "application/json");
        
        var operation = request.SendWebRequest();

        while (!operation.isDone)
            await Task.Yield();
        
        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[SchemaDownloader] Error: {request.responseCode} {request.error}");
            return null;
        }
        
        return request.downloadHandler.text;
    }

    private static string ResolveSchemaUrl()
    {
        var config = Resources.Load<PlayServConfig>("PlayServConfig");
        if (config != null)
            return BuildSchemaUrl(PlayServSettingsResolver.ResolveEditorSettings(config).SchemaApiServerAddress);

        if (PlayServPackageDefaultsProvider.TryLoadSettings(out var packageDefaults))
            return BuildSchemaUrl(packageDefaults.SchemaApiServerAddress);

        throw new InvalidOperationException(
            "schemaApiServerAddress is not configured in PlayServConfig or baked package defaults.");
    }

    private static string BuildSchemaUrl(string serverAddress)
    {
        if (string.IsNullOrWhiteSpace(serverAddress))
            throw new InvalidOperationException("schemaApiServerAddress is required.");

        return serverAddress.Trim().TrimEnd('/') + SchemaBySdkKeyEndpointPath;
    }

    private static void ResetSchemaSelectionProviderIfAvailable()
    {
        if (!PlayServEditorModuleAvailability.RuntimeData)
            return;

        PlayServSchemaSelectionRegistry.TryReset();
    }
}
