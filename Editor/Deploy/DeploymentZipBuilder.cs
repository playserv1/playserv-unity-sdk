#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using UnityEngine;

namespace Playserv.Deploy.Editor
{
    internal sealed class DeploymentZipBuilder
    {
        public byte[] CreateFlatZipArchiveBytes(IReadOnlyList<string> filePaths)
        {
            using var memoryStream = new MemoryStream();

            using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var filePath in filePaths)
                {
                    if (!File.Exists(filePath))
                        continue;

                    archive.CreateEntryFromFile(filePath, Path.GetFileName(filePath));
                }
            }

            return memoryStream.ToArray();
        }

        public string CreateRelativeZipArchive(string rootFolderPath, IReadOnlyList<string> absoluteFiles)
        {
            var zipPath = Path.Combine(Path.GetTempPath(), $"playserv_deploy_{Guid.NewGuid():N}.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(zipPath));

            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                var root = rootFolderPath.Replace('\\', '/').TrimEnd('/');

                foreach (var absoluteFile in absoluteFiles)
                {
                    if (!File.Exists(absoluteFile))
                        continue;

                    var normalized = absoluteFile.Replace('\\', '/');
                    var entryName = normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                        ? normalized.Substring(root.Length).TrimStart('/')
                        : Path.GetFileName(absoluteFile);

                    if (string.IsNullOrWhiteSpace(entryName))
                        entryName = Path.GetFileName(absoluteFile);

                    archive.CreateEntryFromFile(absoluteFile, entryName);
                }
            }

            return zipPath;
        }

        public string ComputeCodeHash(byte[] zipBytes)
        {
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(zipBytes);
            return BitConverter.ToString(hashBytes).Replace("-", string.Empty);
        }

        public void TryDeleteTemp(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PlayServ] Failed to delete temp zip: {e.Message}");
            }
        }
    }
}
#endif
