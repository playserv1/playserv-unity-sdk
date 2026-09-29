using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using Playserv.Editor;
using NUnit.Framework;
using Playserv.ModelGenerator.Editor;

namespace Playserv.Tests.Editor
{
    public sealed class PlayServServerSchemaWorkflowTests
    {
        [Test]
        public void Compare_UsesContentHashInsteadOfOnlyVersion()
        {
            var current = Document("1.0", "hash-a");
            var same = Document("1.0", "hash-a");
            var changedWithoutVersionBump = Document("1.0", "hash-b");

            Assert.That(
                PlayServServerSchemaWorkflow.Compare(current, same),
                Is.EqualTo(PlayServServerSchemaComparison.UpToDate));
            Assert.That(
                PlayServServerSchemaWorkflow.Compare(
                    current,
                    changedWithoutVersionBump),
                Is.EqualTo(PlayServServerSchemaComparison.Different));
        }

        [Test]
        public void Compare_DistinguishesMissingCurrentAndDownload()
        {
            var missing = new PlayServSchemaDocumentInfo(
                "missing.json",
                false,
                string.Empty,
                string.Empty,
                string.Empty,
                0);
            var downloaded = Document("2.0", "hash");

            Assert.That(
                PlayServServerSchemaWorkflow.Compare(missing, missing),
                Is.EqualTo(PlayServServerSchemaComparison.NotDownloaded));
            Assert.That(
                PlayServServerSchemaWorkflow.Compare(missing, downloaded),
                Is.EqualTo(PlayServServerSchemaComparison.NoCurrentSchema));
        }

        [TestCase("PlayerProfile", "PlayerProfile.cs")]
        [TestCase("PlayerProfile.cs", "PlayerProfile.cs")]
        public void GeneratedFileName_AcceptsSimpleNames(
            string modelName,
            string expected)
        {
            Assert.That(
                SchemaCodeGenerator.TryGetGeneratedFileName(
                    modelName,
                    out var fileName),
                Is.True);
            Assert.That(fileName, Is.EqualTo(expected));
        }

        [TestCase("../PlayerProfile")]
        [TestCase("Models/PlayerProfile")]
        [TestCase(@"Models\PlayerProfile")]
        [TestCase("Player:Profile")]
        public void GeneratedFileName_RejectsPathTraversalAndDirectories(
            string modelName)
        {
            Assert.That(
                SchemaCodeGenerator.TryGetGeneratedFileName(
                    modelName,
                    out _),
                Is.False);
        }

        [Test]
        public void GenerateFromMissingSchema_DoesNotReportSuccess()
        {
            var result = SchemaCodeGenerator.GenerateFromSchemaFile(
                "/path/that/does/not/exist/playserv.schema.json",
                acceptAsCurrent: true);

            Assert.That(result.Success, Is.False);
            StringAssert.Contains("was not found", result.Error);
        }

        [Test]
        public void ReadFile_ExtractsServerSchemaMetadata()
        {
            var path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(
                    path,
                    "{\"version\":1,\"jsonSchema\":{\"$defs\":{}," +
                    "\"x-version\":\"7\",\"x-timestamp\":\"2026-07-26T10:00:00Z\"}}");

                var info = PlayServServerSchemaWorkflow.ReadFile(
                    path,
                    "server-schema.json",
                    out var error);

                Assert.That(error, Is.Empty);
                Assert.That(info.Exists, Is.True);
                Assert.That(info.Version, Is.EqualTo("7"));
                Assert.That(info.Timestamp, Is.EqualTo("2026-07-26T10:00:00Z"));
                Assert.That(info.DefinitionCount, Is.EqualTo(0));
                Assert.That(info.Sha256, Has.Length.EqualTo(64));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void MalformedSchema_DoesNotDeleteExistingGeneratedModels()
        {
            var schemaPath = Path.GetTempFileName();
            var generatedDirectory =
                PlayServServerSchemaWorkflow.ToAbsolutePath(
                    PlayServServerSchemaWorkflow.GeneratedModelsAssetPath);
            var sentinelPath = Path.Combine(
                generatedDirectory,
                "PlayServSchemaSafetySentinel.cs");
            Directory.CreateDirectory(generatedDirectory);
            File.WriteAllText(sentinelPath, "// existing generated model");
            File.WriteAllText(schemaPath, "{}");

            try
            {
                var result = SchemaCodeGenerator.GenerateFromSchemaFile(
                    schemaPath,
                    acceptAsCurrent: true);

                Assert.That(result.Success, Is.False);
                Assert.That(File.Exists(sentinelPath), Is.True);
                Assert.That(
                    File.ReadAllText(sentinelPath),
                    Is.EqualTo("// existing generated model"));
            }
            finally
            {
                File.Delete(schemaPath);
                File.Delete(sentinelPath);
            }
        }

        [Test]
        public void RegenerateAfterLocalImport_DoesNotRestoreLegacyDownloadedSchema()
        {
            var currentPath = PlayServServerSchemaWorkflow.ToAbsolutePath(PlayServServerSchemaWorkflow.CurrentSchemaAssetPath);
            var latestPath = PlayServServerSchemaWorkflow.ToAbsolutePath(PlayServServerSchemaWorkflow.LatestSchemaAssetPath);
            var generated = PlayServServerSchemaWorkflow.ToAbsolutePath(PlayServServerSchemaWorkflow.GeneratedModelsAssetPath);
            var paths = new List<string> { currentPath, currentPath + ".meta", latestPath, latestPath + ".meta" };
            if (Directory.Exists(generated)) paths.AddRange(Directory.GetFiles(generated));
            var saved = paths.ToDictionary(path => path, path => File.Exists(path) ? File.ReadAllBytes(path) : null);
            var prefKeys = new[] { Const.PrefKeyJsonSchemaVersion, Const.PrefKeyJsonSchemaTimestamp };
            var savedPrefs = prefKeys.ToDictionary(key => key, key => EditorPrefs.HasKey(key) ? EditorPrefs.GetString(key) : null);
            var importPath = Path.GetTempFileName();
            const string imported = "{\"version\":1,\"jsonSchema\":{\"$defs\":{},\"x-version\":\"new-local\"}}";
            AssetDatabase.StartAssetEditing();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(latestPath));
                File.WriteAllText(latestPath, "{\"version\":1,\"jsonSchema\":{\"$defs\":{},\"x-version\":\"old-download\"}}");
                File.WriteAllText(importPath, imported);
                var result = SchemaCodeGenerator.GenerateFromSchemaFile(importPath, acceptAsCurrent: true);
                Assert.That(result.Success, Is.True, result.Error);

                SchemaCodeGenerator.GenerateModels();

                Assert.That(File.ReadAllText(currentPath), Is.EqualTo(imported));
            }
            finally
            {
                File.Delete(importPath);
                foreach (var entry in savedPrefs)
                {
                    if (entry.Value == null) EditorPrefs.DeleteKey(entry.Key);
                    else EditorPrefs.SetString(entry.Key, entry.Value);
                }
                foreach (var entry in saved)
                {
                    if (entry.Value == null) File.Delete(entry.Key);
                    else File.WriteAllBytes(entry.Key, entry.Value);
                }
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
            }
        }

        [Test]
        public void DownloadSchemaIsRetiredWithoutResolvingCredentials()
        {
            Assert.Throws<NotSupportedException>(() => SchemaLoader.LoadSchema("pk_fixture").GetAwaiter().GetResult());
            Assert.Throws<NotSupportedException>(() => SchemaLoader.LoadSchema(" ").GetAwaiter().GetResult());
        }

        private static PlayServSchemaDocumentInfo Document(
            string version,
            string hash)
        {
            return new PlayServSchemaDocumentInfo(
                "schema.json",
                true,
                version,
                "2026-07-26T00:00:00Z",
                hash,
                1);
        }
    }
}
