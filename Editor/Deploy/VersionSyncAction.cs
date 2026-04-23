#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Wrapper;

namespace Playserv.Deploy.Editor
{
    internal sealed class VersionSyncAction
    {
        private readonly DeploymentZipBuilder _zipBuilder;

        public VersionSyncAction(DeploymentZipBuilder zipBuilder)
        {
            _zipBuilder = zipBuilder ?? throw new ArgumentNullException(nameof(zipBuilder));
        }

        public async Task<VersionSyncResult> ExecuteAsync(
            PlayServConfig config,
            string gameId,
            IReadOnlyList<string> files,
            Action<string> reportStatus,
            CancellationToken ct = default)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            if (string.IsNullOrWhiteSpace(gameId))
                throw new ArgumentException("Game ID is required.", nameof(gameId));

            if (files == null)
                throw new ArgumentNullException(nameof(files));

            reportStatus?.Invoke("Fetching schemas...");
            var api = new DeploymentApiClient(config);

            await api.FetchLatestSchemasAsync(gameId, ct);

            reportStatus?.Invoke("Computing local hash...");
            var localHash = _zipBuilder.ComputeCodeHash(_zipBuilder.CreateFlatZipArchiveBytes(files));

            reportStatus?.Invoke("Fetching remote hash...");
            var remoteHash = await api.GetRemoteCodeHashAsync(gameId, ct);

            if (string.Equals(localHash, remoteHash, StringComparison.OrdinalIgnoreCase))
            {
                reportStatus?.Invoke("Hashes match. Fetching latest version...");
                var latestVersion = await api.GetLatestVersionAsync(gameId, ct);
                return VersionSyncResult.CreateMatched(latestVersion);
            }

            reportStatus?.Invoke("Hash mismatch. Downloading archive...");
            var archiveOutputDir = Path.Combine(Path.GetTempPath(), "playserv-sync");
            var archivePath = await api.DownloadCodeArchiveAsync(gameId, archiveOutputDir, ct);
            return VersionSyncResult.CreateMismatch(archivePath);
        }
    }

    internal sealed class VersionSyncResult
    {
        private VersionSyncResult(bool hashesMatch, string latestVersion, string archivePath)
        {
            HashesMatch = hashesMatch;
            LatestVersion = latestVersion;
            ArchivePath = archivePath;
        }

        public bool HashesMatch { get; }

        public string LatestVersion { get; }

        public string ArchivePath { get; }

        public static VersionSyncResult CreateMatched(string latestVersion)
        {
            return new VersionSyncResult(true, latestVersion, null);
        }

        public static VersionSyncResult CreateMismatch(string archivePath)
        {
            return new VersionSyncResult(false, null, archivePath);
        }
    }
}
#endif
