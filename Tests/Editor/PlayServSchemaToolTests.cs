using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using Playserv.Editor;
using Playserv.Schema;
using UnityEditor.PackageManager;

namespace Playserv.Tests.Editor
{
    public sealed class PlayServSchemaToolTests
    {
        [Test]
        public void PackageDefinition_UsesStandaloneCompanionPackage()
        {
            var definition = PlayServSchemaToolPackage.Definition;

            Assert.That(definition.PackageId, Is.EqualTo("com.playserv.schema-tool"));
            Assert.That(definition.ModuleId, Is.EqualTo("editor-schema-tool"));
            Assert.That(
                definition.GitPath,
                Is.EqualTo("CompanionPackages~/com.playserv.schema-tool"));
        }

        [Test]
        public void RuntimeAnnotations_KeepStableContractMetadata()
        {
            var schema = new PlayServSchemaAttribute("player.profile")
            {
                Authority = PlayServSchemaAuthority.Client,
                Version = "2"
            };
            var field = new PlayServFieldAttribute("playerId")
            {
                Required = PlayServRequiredMode.Required
            };

            Assert.That(schema.Id, Is.EqualTo("player.profile"));
            Assert.That(schema.Authority, Is.EqualTo(PlayServSchemaAuthority.Client));
            Assert.That(schema.Version, Is.EqualTo("2"));
            Assert.That(field.Name, Is.EqualTo("playerId"));
            Assert.That(field.Required, Is.EqualTo(PlayServRequiredMode.Required));
        }

        [Test]
        public void ShippedTool_RunsOnUnityBundledDotNet()
        {
            var dotnetPath = PlayServSchemaToolRunner.ResolveBundledDotNet();
            Assert.That(File.Exists(dotnetPath), Is.True, dotnetPath);

            var corePackage = PackageInfo.FindForAssetPath(
                "Packages/com.playserv.sdk/package.json");
            Assert.That(corePackage, Is.Not.Null);
            var companionPackage = PackageInfo.FindForAssetPath(
                "Packages/com.playserv.schema-tool/package.json");
            var toolPath = companionPackage != null
                ? Path.Combine(
                    companionPackage.resolvedPath,
                    PlayServSchemaToolPackage.ToolRelativePath)
                : Path.Combine(
                    corePackage.resolvedPath,
                    "CompanionPackages~/com.playserv.schema-tool",
                    PlayServSchemaToolPackage.ToolRelativePath);
            if (!File.Exists(toolPath))
                Assert.Ignore("Schema Tool companion source is not present in this package layout.");

            var startInfo = new ProcessStartInfo
            {
                FileName = dotnetPath,
                Arguments = $"\"{toolPath}\" version --json",
                WorkingDirectory = corePackage.resolvedPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using (var process = Process.Start(startInfo))
            {
                Assert.That(process, Is.Not.Null);
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                Assert.That(process.WaitForExit(15000), Is.True, "Schema Tool timed out.");
                Assert.That(process.ExitCode, Is.EqualTo(0), error);
                StringAssert.Contains("\"version\": \"0.3.3\"", output);
                StringAssert.Contains("\"protocolVersion\": 1", output);
            }
        }
    }
}
