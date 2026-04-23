#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Playserv.Wrapper;

namespace Playserv.Deploy.Editor
{
    internal sealed class DeploymentUploadAction
    {
        private readonly DeploymentZipBuilder _zipBuilder;

        public DeploymentUploadAction(DeploymentZipBuilder zipBuilder)
        {
            _zipBuilder = zipBuilder ?? throw new ArgumentNullException(nameof(zipBuilder));
        }

        public async Task ExecuteAsync(
            PlayServConfig config,
            string gameId,
            IReadOnlyList<string> files,
            string rootFolderPath,
            bool keepRelativePaths,
            Action<string, float> reportProgress,
            CancellationToken ct = default)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            if (string.IsNullOrWhiteSpace(gameId))
                throw new ArgumentException("Game ID is required.", nameof(gameId));

            if (files == null)
                throw new ArgumentNullException(nameof(files));

            var api = new DeploymentApiClient(config);

            if (keepRelativePaths)
            {
                try
                {
                    await UploadRelativePathArchiveAsync(api, gameId, files, rootFolderPath, reportProgress, ct);
                    return;
                }
                catch (InvalidOperationException e) when (IsNativeAotFailure(e) || IsInvalidZipUploadFailure(e))
                {
                    Debug.LogWarning("[PlayServ] Relative-path ZIP upload failed. Retrying with flat ZIP packaging.");
                }
            }

            reportProgress?.Invoke("Uploading ZIP...", 0.55f);
            var service = new DeploymentService(api);
            await service.DeployAsync(gameId, files.ToList(), ct);
            reportProgress?.Invoke("Upload finished.", 0.95f);
        }

        private async Task UploadRelativePathArchiveAsync(
            DeploymentApiClient api,
            string gameId,
            IReadOnlyList<string> files,
            string rootFolderPath,
            Action<string, float> reportProgress,
            CancellationToken ct)
        {
            reportProgress?.Invoke("Creating ZIP...", 0.15f);
            var zipPath = _zipBuilder.CreateRelativeZipArchive(rootFolderPath, files);

            try
            {
                reportProgress?.Invoke("Uploading ZIP...", 0.55f);
                await api.UploadDeploymentAsync(gameId, zipPath, ct);
                reportProgress?.Invoke("Upload finished.", 0.95f);
            }
            finally
            {
                _zipBuilder.TryDeleteTemp(zipPath);
            }
        }

        private static bool IsNativeAotFailure(Exception exception)
        {
            if (exception == null)
                return false;

            var message = exception.Message ?? string.Empty;
            return message.IndexOf("Native AOT compilation failed", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsInvalidZipUploadFailure(Exception exception)
        {
            if (exception == null)
                return false;

            var message = exception.Message ?? string.Empty;
            return message.IndexOf("Request body is empty", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("valid ZIP archive", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
#endif
