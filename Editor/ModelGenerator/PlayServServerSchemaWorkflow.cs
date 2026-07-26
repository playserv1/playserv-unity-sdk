using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Playserv.CodeGenerator;
using UnityEngine;

namespace Playserv.ModelGenerator.Editor
{
    internal enum PlayServServerSchemaComparison
    {
        NotDownloaded,
        NoCurrentSchema,
        UpToDate,
        Different
    }

    internal sealed class PlayServSchemaDocumentInfo
    {
        public PlayServSchemaDocumentInfo(
            string assetPath,
            bool exists,
            string version,
            string timestamp,
            string sha256,
            int definitionCount)
        {
            AssetPath = assetPath ?? string.Empty;
            Exists = exists;
            Version = version ?? string.Empty;
            Timestamp = timestamp ?? string.Empty;
            Sha256 = sha256 ?? string.Empty;
            DefinitionCount = definitionCount;
        }

        public string AssetPath { get; }

        public bool Exists { get; }

        public string Version { get; }

        public string Timestamp { get; }

        public string Sha256 { get; }

        public int DefinitionCount { get; }

        public string DisplayVersion =>
            string.IsNullOrWhiteSpace(Version) ? "Unknown" : Version;

        public string DisplayTimestamp
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Timestamp))
                    return "No timestamp";

                return DateTimeOffset.TryParse(
                    Timestamp,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal,
                    out var value)
                    ? value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
                    : Timestamp;
            }
        }
    }

    internal static class PlayServServerSchemaWorkflow
    {
        public const string LatestSchemaAssetPath =
            "Assets/Resources/latest-schema.json";
        public const string CurrentSchemaAssetPath =
            "Assets/Resources/current-schema.json";
        public const string GeneratedModelsAssetPath =
            "Assets/Shared/Generated/Models";

        public static PlayServSchemaDocumentInfo ReadLatest(out string error)
        {
            return ReadAsset(LatestSchemaAssetPath, out error);
        }

        public static PlayServSchemaDocumentInfo ReadCurrent(out string error)
        {
            return ReadAsset(CurrentSchemaAssetPath, out error);
        }

        public static PlayServServerSchemaComparison Compare(
            PlayServSchemaDocumentInfo current,
            PlayServSchemaDocumentInfo latest)
        {
            if (latest == null || !latest.Exists)
                return PlayServServerSchemaComparison.NotDownloaded;
            if (current == null || !current.Exists)
                return PlayServServerSchemaComparison.NoCurrentSchema;

            return string.Equals(
                current.Sha256,
                latest.Sha256,
                StringComparison.OrdinalIgnoreCase)
                ? PlayServServerSchemaComparison.UpToDate
                : PlayServServerSchemaComparison.Different;
        }

        internal static PlayServSchemaDocumentInfo ReadAsset(
            string assetPath,
            out string error)
        {
            var absolutePath = ToAbsolutePath(assetPath);
            return ReadFile(absolutePath, assetPath, out error);
        }

        internal static PlayServSchemaDocumentInfo ReadFile(
            string absolutePath,
            string displayPath,
            out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(absolutePath) || !File.Exists(absolutePath))
            {
                return new PlayServSchemaDocumentInfo(
                    displayPath,
                    false,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    0);
            }

            try
            {
                var content = File.ReadAllText(absolutePath);
                var root = SchemaJsonReader.ReadRoot(content);
                if (!SchemaJsonReader.IsSupportedRoot(root))
                {
                    error = $"{displayPath} does not contain a supported PlayServ schema.";
                    return new PlayServSchemaDocumentInfo(
                        displayPath,
                        true,
                        string.Empty,
                        string.Empty,
                        ComputeSha256(content),
                        0);
                }

                return new PlayServSchemaDocumentInfo(
                    displayPath,
                    true,
                    root.JsonSchema.XVersion,
                    root.JsonSchema.XTimestamp,
                    ComputeSha256(content),
                    SchemaUtils.GetAllDefinitions(root.JsonSchema).Count());
            }
            catch (Exception exception)
            {
                error =
                    $"Failed to read {displayPath}: {exception.GetBaseException().Message}";
                return new PlayServSchemaDocumentInfo(
                    displayPath,
                    true,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    0);
            }
        }

        internal static string ToAbsolutePath(string assetPath)
        {
            var projectRoot = Path.GetFullPath(
                Path.Combine(Application.dataPath, ".."));
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath ?? string.Empty));
        }

        private static string ComputeSha256(string content)
        {
            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(
                    Encoding.UTF8.GetBytes(content ?? string.Empty));
                return BitConverter.ToString(hash).Replace("-", string.Empty)
                    .ToLowerInvariant();
            }
        }
    }
}
