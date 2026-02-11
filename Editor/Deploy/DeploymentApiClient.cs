using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Wrapper;
using UnityEngine;
using UnityEngine.Networking;

namespace Playserv.Deploy.Editor
{
    public sealed class DeploymentApiClient
    {
        private readonly PlayServConfig _settings;

        public DeploymentApiClient(PlayServConfig settings)
        {
            _settings = settings;
        }

        public async Task UploadDeploymentAsync(string gameId, string zipPath, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(_settings.DeployApiEndpoint))
                throw new InvalidOperationException("DeploymentSettings.ApiEndpoint is empty.");

            if (!File.Exists(zipPath))
                throw new FileNotFoundException("ZIP file not found.", zipPath);

            var url = $"{_settings.DeployApiEndpoint}?gameId={UnityWebRequest.EscapeURL(gameId)}";

            // Read ZIP bytes
            var data = File.ReadAllBytes(zipPath);

            using var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            req.uploadHandler = new UploadHandlerRaw(data);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/zip");

            // if (!string.IsNullOrWhiteSpace(_settings.BearerToken))
            //     req.SetRequestHeader("Authorization", $"Bearer {_settings.BearerToken}");

            req.timeout = Mathf.Max(1, _settings.TimeoutSeconds);

            var op = req.SendWebRequest();

            // Await UnityWebRequest in editor without coroutines
            while (!op.isDone)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(50, ct);
            }

#if UNITY_2020_2_OR_NEWER
            if (req.result != UnityWebRequest.Result.Success)
#else
            if (req.isNetworkError || req.isHttpError)
#endif
            {
                throw new InvalidOperationException(
                    $"Upload failed. HTTP {(int)req.responseCode}. Error: {req.error}. Body: {req.downloadHandler?.text}"
                );
            }
        }
    }
}