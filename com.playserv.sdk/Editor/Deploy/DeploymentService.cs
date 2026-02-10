using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Playserv.Deploy.Editor
{
    public sealed class DeploymentService
    {
        private readonly DeploymentApiClient _apiClient;

        public DeploymentService(DeploymentApiClient apiClient)
        {
            _apiClient = apiClient;
        }

        public async Task DeployAsync(string gameId, List<string> filesToDeploy, CancellationToken ct = default)
        {
            Debug.Log($"[PlayServ] Starting deployment of {filesToDeploy.Count} file(s)");
            foreach (var file in filesToDeploy)
                Debug.Log($"[PlayServ]  - {file}");

            var zipPath = await CreateZipArchiveAsync(filesToDeploy);

            try
            {
                await _apiClient.UploadDeploymentAsync(gameId, zipPath, ct);
                Debug.Log("[PlayServ] Deployment completed successfully");
            }
            finally
            {
                TryDelete(zipPath);
            }
        }

        private static Task<string> CreateZipArchiveAsync(List<string> filesToDeploy)
        {
            var tempZipPath = Path.Combine(Path.GetTempPath(), $"deployment_{Guid.NewGuid():N}.zip");
            Debug.Log($"[PlayServ] Creating ZIP archive: {tempZipPath}");

            // Ensure directory exists (usually does)
            Directory.CreateDirectory(Path.GetDirectoryName(tempZipPath)!);

            using (var archive = ZipFile.Open(tempZipPath, ZipArchiveMode.Create))
            {
                foreach (var filePath in filesToDeploy)
                {
                    if (!File.Exists(filePath))
                    {
                        Debug.LogWarning($"[PlayServ] File not found: {filePath}");
                        continue;
                    }

                    // Keep file name only (like your original code)
                    var entryName = Path.GetFileName(filePath);

                    // If you might have duplicate file names, you can change entryName to a relative path instead.
                    archive.CreateEntryFromFile(filePath, entryName);
                }
            }

            var size = new FileInfo(tempZipPath).Length;
            Debug.Log($"[PlayServ] ZIP archive created: {size} bytes");

            return Task.FromResult(tempZipPath);
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    Debug.Log($"[PlayServ] Cleaned up temporary ZIP file: {path}");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PlayServ] Failed to delete temp zip: {path}. {e.Message}");
            }
        }
    }
}