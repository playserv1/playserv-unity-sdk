#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
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
        var selectedEnvironment = PlayServEnvironmentResolver.ResolveEnvironmentName(null);
        PlayServEnvironmentConfig config;

        if (!PlayServEnvironmentResolver.TryLoadConfigFromFile(out config, out var error))
        {
            config = PlayServEnvironmentConfig.CreateDefault();
            Debug.LogWarning($"[SchemaDownloader] {error} Falling back to default environment profiles.");
        }

        selectedEnvironment = PlayServEnvironmentResolver.ResolveEnvironmentName(config.ActiveEnvironment);
        if (!config.TryGetProfile(selectedEnvironment, out var profile) ||
            string.IsNullOrWhiteSpace(profile.SchemaApiServerAddress))
        {
            if (PlayServEnvDefaultsProvider.TryLoadAsset(out var envDefaults) &&
                !string.IsNullOrWhiteSpace(envDefaults.SchemaApiServerAddress))
            {
                selectedEnvironment = string.IsNullOrWhiteSpace(envDefaults.EnvironmentName)
                    ? PlayServEnvironmentResolver.DefaultEnvironment
                    : envDefaults.EnvironmentName;

                return BuildSchemaUrl(envDefaults.SchemaApiServerAddress, selectedEnvironment);
            }

            profile = PlayServEnvironmentProfile.CreateDevDefaults();
            selectedEnvironment = PlayServEnvironmentResolver.DevEnvironment;
            Debug.LogWarning("[SchemaDownloader] schemaApiServerAddress is not configured for selected environment. Falling back to dev defaults.");
        }

        return BuildSchemaUrl(profile.SchemaApiServerAddress, selectedEnvironment);
    }

    private static string BuildSchemaUrl(string serverAddress, string environmentName)
    {
        if (string.IsNullOrWhiteSpace(serverAddress))
            throw new InvalidOperationException(
                $"schemaApiServerAddress is required for environment '{environmentName}'.");

        return serverAddress.Trim().TrimEnd('/') + SchemaBySdkKeyEndpointPath;
    }
}
#endif
