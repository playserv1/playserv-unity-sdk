using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using Playserv.Editor;
using Playserv.Schema;
using UnityEditor;
using UnityEngine;
using PackageManagerPackageInfo = UnityEditor.PackageManager.PackageInfo;

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
                Version = "2",
                Description = "Player profile",
                Kind = PlayServSchemaKind.Entity,
                OwnedBy = PlayServSchemaOwner.Player,
                Read = PlayServSchemaReadPolicy.Owner,
                OnPlayerDelete = PlayServPlayerDeletePolicy.CascadeDelete,
                ClientRead = PlayServSchemaAccess.Allow,
                ClientWrite = PlayServSchemaAccess.Allow
            };
            var field = new PlayServFieldAttribute("playerId")
            {
                Required = PlayServRequiredMode.Required,
                Type = PlayServSchemaFieldType.Uuid,
                CodeKey = "player.profile.id",
                Primary = true,
                Indexed = true
            };

            Assert.That(schema.Id, Is.EqualTo("player.profile"));
            Assert.That(schema.Authority, Is.EqualTo(PlayServSchemaAuthority.Client));
            Assert.That(schema.Version, Is.EqualTo("2"));
            Assert.That(schema.Kind, Is.EqualTo(PlayServSchemaKind.Entity));
            Assert.That(schema.OwnedBy, Is.EqualTo(PlayServSchemaOwner.Player));
            Assert.That(schema.ClientWrite, Is.EqualTo(PlayServSchemaAccess.Allow));
            Assert.That(field.Name, Is.EqualTo("playerId"));
            Assert.That(field.Required, Is.EqualTo(PlayServRequiredMode.Required));
            Assert.That(field.Type, Is.EqualTo(PlayServSchemaFieldType.Uuid));
            Assert.That(field.CodeKey, Is.EqualTo("player.profile.id"));
            Assert.That(field.Primary, Is.True);
        }

        [Test]
        public void ShippedTool_RunsOnUnityBundledDotNet()
        {
            var dotnetPath = PlayServSchemaToolRunner.ResolveBundledDotNet();
            Assert.That(File.Exists(dotnetPath), Is.True, dotnetPath);
            var toolPath = ResolveShippedToolPath();

            var result = RunTool(dotnetPath, toolPath, "version --json");
            Assert.That(result.ExitCode, Is.EqualTo(0), result.Error);
            StringAssert.Contains("\"version\": \"0.6.8\"", result.Output);
            StringAssert.Contains("\"protocolVersion\": 1", result.Output);
        }

        [Test]
        public void ShippedTool_TargetsUnity2021LinuxRuntime()
        {
            var toolPath = ResolveShippedToolPath();
            var runtimeConfigPath = Path.ChangeExtension(toolPath, ".runtimeconfig.json");
            var dependencyManifestPath = Path.ChangeExtension(toolPath, ".deps.json");
            Assert.That(File.Exists(runtimeConfigPath), Is.True, runtimeConfigPath);
            Assert.That(File.Exists(dependencyManifestPath), Is.True, dependencyManifestPath);

            var runtimeConfig = File.ReadAllText(runtimeConfigPath);
            StringAssert.Contains("\"tfm\": \"net5.0\"", runtimeConfig);
            StringAssert.Contains("\"version\": \"5.0.0\"", runtimeConfig);
            StringAssert.Contains("\"rollForward\": \"LatestMajor\"", runtimeConfig);

            var dependencyManifest = File.ReadAllText(dependencyManifestPath);
            StringAssert.Contains(
                "\"System.Collections.Immutable/6.0.0\"",
                dependencyManifest);
            StringAssert.Contains(
                "\"System.Runtime.CompilerServices.Unsafe/6.0.0\"",
                dependencyManifest);
            StringAssert.Contains(
                "\"System.Text.Encoding.CodePages/6.0.0\"",
                dependencyManifest);

            var runtimeDirectory = Path.GetDirectoryName(toolPath);
            foreach (var dependency in new[]
                     {
                         "System.Collections.Immutable.dll",
                         "System.Runtime.CompilerServices.Unsafe.dll",
                         "System.Text.Encoding.CodePages.dll"
                     })
            {
                Assert.That(
                    File.Exists(Path.Combine(runtimeDirectory, dependency)),
                    Is.True,
                    dependency);
            }
        }

        [Test]
        public void ShippedTool_RoslynDependenciesUsePortableImages()
        {
            var runtimeDirectory = Path.GetDirectoryName(ResolveShippedToolPath());
            foreach (var dependency in new[]
                     {
                         "Microsoft.CodeAnalysis.dll",
                         "Microsoft.CodeAnalysis.CSharp.dll"
                     })
            {
                var dependencyPath = Path.Combine(runtimeDirectory, dependency);
                Assert.That(File.Exists(dependencyPath), Is.True, dependencyPath);
                Assert.That(
                    ReadPortableExecutableMagic(dependencyPath),
                    Is.EqualTo(0x10b),
                    dependency +
                    " must be a portable PE32 IL image, not a platform-specific PE32+ ReadyToRun image.");
            }
        }

        [Test]
        public void ShippedTool_AnalyzesAndGeneratesOnUnityBundledDotNet()
        {
            var dotnetPath = PlayServSchemaToolRunner.ResolveBundledDotNet();
            Assert.That(File.Exists(dotnetPath), Is.True, dotnetPath);
            var toolPath = ResolveShippedToolPath();
            var projectRoot = Path.Combine(
                Path.GetTempPath(),
                "playserv-schema-tool-tests-" + System.Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(projectRoot);
            try
            {
                var initialize = RunTool(
                    dotnetPath,
                    toolPath,
                    $"init --project \"{projectRoot}\" --json");
                Assert.That(initialize.ExitCode, Is.EqualTo(0), initialize.Error);

                var assetsPath = Path.Combine(projectRoot, "Assets");
                Directory.CreateDirectory(assetsPath);
                File.WriteAllText(
                    Path.Combine(assetsPath, "PlayerProfile.cs"),
                    "[PlayServSchema(\"player.profile\")]\n" +
                    "public sealed class PlayerProfile\n" +
                    "{\n" +
                    "    public string DisplayName { get; set; }\n" +
                    "}\n");

                var generate = RunTool(
                    dotnetPath,
                    toolPath,
                    $"generate --project \"{projectRoot}\" --json");
                Assert.That(generate.ExitCode, Is.EqualTo(0), generate.Error);
                StringAssert.Contains("\"schemaCount\": 1", generate.Output);
                Assert.That(
                    File.Exists(Path.Combine(
                        projectRoot,
                        "Assets/PlayServ/Generated/Schemas/playserv.schema.json")),
                    Is.True);

                var dryRun = RunTool(
                    dotnetPath,
                    toolPath,
                    $"push --dry-run --project \"{projectRoot}\" --json");
                Assert.That(dryRun.ExitCode, Is.EqualTo(0), dryRun.Error);
                StringAssert.Contains("\"dryRun\": true", dryRun.Output);
                StringAssert.Contains("\"pushedSchemaCount\": 1", dryRun.Output);
            }
            finally
            {
                if (Directory.Exists(projectRoot))
                    Directory.Delete(projectRoot, true);
            }
        }

        [Test]
        public void ShippedTool_TypedReferencesGenerateIDSchemaAndPushRelationCardinality()
        {
            var root = Path.Combine(Path.GetTempPath(), "playserv-ref-schema-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var dotnet = PlayServSchemaToolRunner.ResolveBundledDotNet();
            var tool = ResolveShippedToolPath();
            try
            {
                Assert.That(RunTool(dotnet, tool, $"init --project \"{root}\" --json").ExitCode, Is.Zero);
                Directory.CreateDirectory(Path.Combine(root, "Assets"));
                File.WriteAllText(Path.Combine(root, "Assets/Refs.cs"),
                    "[PlayServSchema(\"child\")] public class Child { public string Name; }\n" +
                    "[PlayServSchema(\"parent\")] public class Parent { public PlayServRecordRef<Child> Child; public System.Collections.Generic.List<Playserv.Data.PlayServRecordRef<Child>> Children; public PlayServRecordRef<Child>[]? OptionalArray; public System.Collections.Generic.List<PlayServRecordRef<Child>>? OptionalList; }");
                var generated = RunTool(dotnet, tool, $"generate --project \"{root}\" --json");
                Assert.That(generated.ExitCode, Is.Zero, generated.Output + generated.Error);
                var schema = File.ReadAllText(Path.Combine(root, "Assets/PlayServ/Generated/Schemas/playserv.schema.json"));
                StringAssert.DoesNotContain("x-playserv-unresolved-csharp-type", schema);
                var definitions = Newtonsoft.Json.Linq.JObject.Parse(schema)["$defs"];
                Assert.That((string)definitions["parent"]["properties"]["Child"]["type"], Is.EqualTo("string"));
                Assert.That((string)definitions["parent"]["properties"]["Children"]["items"]["type"], Is.EqualTo("string"));

                var portProbe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
                portProbe.Start(); var port = ((System.Net.IPEndPoint)portProbe.LocalEndpoint).Port; portProbe.Stop();
                using var listener = new System.Net.HttpListener();
                listener.Prefixes.Add($"http://127.0.0.1:{port}/"); listener.Start();
                var serve = System.Threading.Tasks.Task.Run(async () =>
                {
                    string payload = null;
                    for (var index = 0; index < 3; index++)
                    {
                        var context = await listener.GetContextAsync();
                        var reply = index == 0 ? "{\"access_token\":\"fixture\",\"project_id\":\"p\",\"project_slug\":\"p\",\"env\":\"dev\"}" : "{\"revision\":\"v1\"}";
                        if (index == 2) using (var reader = new StreamReader(context.Request.InputStream)) payload = await reader.ReadToEndAsync();
                        var bytes = System.Text.Encoding.UTF8.GetBytes(reply);
                        context.Response.ContentType = "application/json";
                        await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length); context.Response.Close();
                    }
                    return payload;
                });
                var push = RunTool(dotnet, tool, $"push --project \"{root}\" --endpoint http://127.0.0.1:{port} --json", "sk_fixture");
                Assert.That(push.ExitCode, Is.Zero, push.Output + push.Error);
                Assert.That(serve.Wait(5000), Is.True);
                var entities = Newtonsoft.Json.Linq.JObject.Parse(serve.Result)["entities"];
                var parent = System.Linq.Enumerable.Single(entities, e => (string)e["code_key"] == "parent");
                var fields = parent["entity"]["fields"];
                Assert.That((string)fields[0]["type"], Is.EqualTo("relation"));
                Assert.That((string)fields[0]["target"], Is.EqualTo("Child"));
                Assert.That((string)fields[0]["cardinality"], Is.EqualTo("one"));
                Assert.That((string)fields[1]["type"], Is.EqualTo("relation"));
                Assert.That((string)fields[1]["target"], Is.EqualTo("Child"));
                Assert.That((string)fields[1]["cardinality"], Is.EqualTo("many"));
                foreach (var index in new[] { 2, 3 })
                {
                    Assert.That((string)fields[index]["type"], Is.EqualTo("relation"));
                    Assert.That((string)fields[index]["target"], Is.EqualTo("Child"));
                    Assert.That((string)fields[index]["cardinality"], Is.EqualTo("many"));
                }
            }
            finally { Directory.Delete(root, true); }
        }

        [Test]
        public void ShippedTool_GeneratedCSharpReferencesResolveWithoutSourceUsings()
        {
            var root = Path.Combine(Path.GetTempPath(), "playserv-ref-csharp-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var dotnet = PlayServSchemaToolRunner.ResolveBundledDotNet(); var tool = ResolveShippedToolPath();
            try
            {
                Assert.That(RunTool(dotnet, tool, $"init --project \"{root}\" --json").ExitCode, Is.Zero);
                var configPath = Path.Combine(root, "playserv.schema.json");
                var config = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(configPath));
                ((Newtonsoft.Json.Linq.JArray)config["targets"]).Add(new Newtonsoft.Json.Linq.JObject
                    { ["kind"] = "csharp-contracts", ["output"] = "Generated/Contracts.cs" });
                File.WriteAllText(configPath, config.ToString());
                Directory.CreateDirectory(Path.Combine(root, "Assets"));
                File.WriteAllText(Path.Combine(root, "Assets/Refs.cs"),
                    "using Playserv.Data; [PlayServSchema(\"child\")] public class Child { public string Name; } " +
                    "[PlayServSchema(\"parent\")] public class Parent { public PlayServRecordRef<Child> Child; public PlayServRecordRef<Child>[] Children; }");
                var generate = RunTool(dotnet, tool, $"generate --project \"{root}\" --json");
                Assert.That(generate.ExitCode, Is.Zero, generate.Output + generate.Error);
                var code = File.ReadAllText(Path.Combine(root, "Generated/Contracts.cs"));
                StringAssert.Contains("global::Playserv.Data.PlayServRecordRef<", code);
            }
            finally { Directory.Delete(root, true); }
        }

        [Test]
        public void BundledDotNetCandidates_CoverUnity2021ThroughUnity66Layouts()
        {
            var macCandidates = PlayServSchemaToolRunner.BuildBundledDotNetCandidates(
                "/Applications/Unity/Unity.app/Contents",
                "dotnet");
            CollectionAssert.Contains(
                macCandidates,
                Path.Combine("/Applications/Unity/Unity.app/Contents", "NetCoreRuntime", "dotnet"));
            CollectionAssert.Contains(
                macCandidates,
                Path.Combine("/Applications/Unity/Unity.app/Contents", "Resources", "Scripting", "NetCoreRuntime", "dotnet"));

            var windowsCandidates = PlayServSchemaToolRunner.BuildBundledDotNetCandidates(
                @"C:\Unity\Editor\Data",
                "dotnet.exe");
            Assert.That(
                windowsCandidates,
                Has.Some.EndsWith(
                    Path.Combine("NetCoreRuntime", "dotnet.exe")));
            Assert.That(
                windowsCandidates,
                Has.Some.EndsWith(
                    Path.Combine(
                        "Resources",
                        "Scripting",
                        "NetCoreRuntime",
                        "dotnet.exe")));

            Assert.That(
                PlayServSchemaToolRunner.ResolveBundledDotNet(
                    EditorApplication.applicationContentsPath,
                    Application.platform),
                Is.EqualTo(PlayServSchemaToolRunner.ResolveBundledDotNet()));
        }

        private static string ResolveShippedToolPath()
        {
            var corePackage = PackageManagerPackageInfo.FindForAssetPath(
                "Packages/com.playserv.sdk/package.json");
            Assert.That(corePackage, Is.Not.Null);
            var companionPackage = PackageManagerPackageInfo.FindForAssetPath(
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
            return toolPath;
        }

        private static ToolProcessResult RunTool(
            string dotnetPath,
            string toolPath,
            string arguments, string serverKey = null)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = dotnetPath,
                Arguments = $"\"{toolPath}\" {arguments}",
                WorkingDirectory = Path.GetDirectoryName(toolPath),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            if (serverKey != null) startInfo.EnvironmentVariables["PLAYSERV_SERVER_KEY"] = serverKey;
            using (var process = Process.Start(startInfo))
            {
                Assert.That(process, Is.Not.Null);
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                Assert.That(process.WaitForExit(15000), Is.True, "Schema Tool timed out.");
                return new ToolProcessResult(process.ExitCode, output, error);
            }
        }

        private static int ReadPortableExecutableMagic(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var reader = new BinaryReader(stream))
            {
                stream.Position = 0x3c;
                var peHeaderOffset = reader.ReadInt32();
                stream.Position = peHeaderOffset;
                Assert.That(reader.ReadUInt32(), Is.EqualTo(0x00004550u), path);
                stream.Position = peHeaderOffset + 24;
                return reader.ReadUInt16();
            }
        }

        private sealed class ToolProcessResult
        {
            public ToolProcessResult(int exitCode, string output, string error)
            {
                ExitCode = exitCode;
                Output = output;
                Error = error;
            }

            public int ExitCode { get; }
            public string Output { get; }
            public string Error { get; }
        }
    }
}
