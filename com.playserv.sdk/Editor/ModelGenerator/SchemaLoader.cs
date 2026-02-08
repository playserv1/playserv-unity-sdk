#if UNITY_EDITOR
using System.IO;
using System.Threading.Tasks;
using Playserv.ModelGenerator.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

public static class SchemaLoader
{
    private const string Url =
        "https://playserv-backoffice.test.playserv.io/api/projects/{0}/schemas"; 
    private const string LatestSchemaFileName = "latest-schema.json";
    
    public static void LoadSchema(string gameId) => _ = DownloadSchema(gameId);

    public static void CheckNewSchema()
    {
        SchemaCodeGenerator.CheckNewVersionJsonSchema();
    }
    
    private static async Task DownloadSchema(string gameId)
    {
        var gameSchemaUrl = string.Format(Url, gameId);
        
        await DownloadAndSaveToResourcesAsync(gameSchemaUrl);
    }
    
    private static async Task DownloadAndSaveToResourcesAsync(string url)
    {
        var json = await LoadJson(url);
        
        var resourcesDir = Path.Combine(Application.dataPath, "Resources");
        if (!Directory.Exists(resourcesDir))
            Directory.CreateDirectory(resourcesDir);

        var filePath = Path.Combine(resourcesDir, LatestSchemaFileName);
        await File.WriteAllTextAsync(filePath, json);
        
        AssetDatabase.Refresh();

        Debug.Log($"[SchemaDownloader] schema.json saved to {filePath}");
    }

    private static async Task<string> LoadJson(string url)
    {
        using var request = UnityWebRequest.Get(url);
        request.SetRequestHeader("Accept", "application/json");

        var operation = request.SendWebRequest();

        while (!operation.isDone)
            await Task.Yield();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[SchemaDownloader] Error: {request.error}");
            return null;
        }

        var json = request.downloadHandler.text;
        return json;
    }
}
#endif