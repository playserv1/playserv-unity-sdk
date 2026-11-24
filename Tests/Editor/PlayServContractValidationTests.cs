using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Playserv.Editor;
using Playserv.Schema;
using UnityEditor.PackageManager;
using UnityEditor.Compilation;

namespace Playserv.Tests.Editor
{
    public sealed class PlayServContractValidationTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "playserv-contract-validation-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        [Test]
        public void DuplicateFieldFailsWithRuleAndMemberLocationWithoutSchemaConfiguration()
        {
            var result = Validate("using Playserv.Schema;\n[PlayServSchema(\"profile\")] public class Profile {\n" +
                                  "[PlayServField(\"name\")] public string First;\n" +
                                  "[PlayServField(\"name\")] public string Second;\n}");
            Assert.That(result.ExitCode, Is.EqualTo(1), result.Output);
            var diagnostics = (JArray)JObject.Parse(result.Output)["diagnostics"];
            Assert.That(diagnostics.Count, Is.EqualTo(2));
            Assert.That(diagnostics.All(d => (string)d["code"] == "PSSCHEMA003"), Is.True);
            Assert.That(diagnostics.Select(d => (string)d["member"]), Is.EqualTo(new[] { "First", "Second" }));
            Assert.That(diagnostics.Select(d => (int)d["line"]), Is.EqualTo(new[] { 3, 4 }));
            Assert.That(diagnostics.All(d => (string)d["path"] == "Contract.cs" && (int)d["column"] > 0), Is.True);
        }

        [Test]
        public void DuplicateIdsAcrossFilesFailDeterministically()
        {
            const string first = "using Playserv.Schema; [PlayServSchema(\"same\")] public class First {}";
            const string second = "using Playserv.Schema; [PlayServSchema(\"same\")] public class Second {}";
            var result = Validate(first, second);
            Assert.That(result.ExitCode, Is.EqualTo(1), result.Output);
            Assert.That(JObject.Parse(result.Output)["diagnostics"].Select(d => (string)d["code"]),
                Is.EqualTo(new[] { "PSSCHEMA001", "PSSCHEMA001" }));
            Assert.That(Validate(first, second).Output, Is.EqualTo(result.Output));
        }

        [Test]
        public void PartialMembersAreValidatedTogetherWithAttributeAliasesAndConstants()
        {
            var result = Validate("using S = Playserv.Schema.PlayServSchemaAttribute; using Playserv.Schema;\n" +
                "[S(Id)] public partial class Profile { const string Id = \"profile\"; [PlayServField(\"name\")] public string First; }",
                "using Playserv.Schema; public partial class Profile { [PlayServField(\"name\")] public string Second; }");
            Assert.That(result.ExitCode, Is.EqualTo(1), result.Output);
            Assert.That(JObject.Parse(result.Output)["diagnostics"].Select(d => (string)d["code"]),
                Is.EqualTo(new[] { "PSSCHEMA003", "PSSCHEMA003" }));
        }

        [Test]
        public void ValidContractsRespectBuildDefinesAndDoNotWriteSchemasOrLocks()
        {
            File.WriteAllText(Path.Combine(_root, "playserv.schema.json"), "deliberately unrelated generation configuration");
            File.WriteAllText(Path.Combine(_root, "playserv.schema.lock.json"), "unchanged");
            var result = Validate("using Playserv.Schema;\n#if UNITY_SERVER\n" +
                "[PlayServSchema(\"server\")] public class ServerContract { public int Score; }\n#endif\n" +
                "#if UNITY_EDITOR\n[PlayServSchema(\"same\")] public class EditorOnly {}\n" +
                "[PlayServSchema(\"same\")] public class AnotherEditorOnly {}\n#endif");
            Assert.That(result.ExitCode, Is.Zero, result.Output);
            Assert.That((int)JObject.Parse(result.Output)["schemaCount"], Is.EqualTo(1));
            Assert.That((JArray)JObject.Parse(result.Output)["diagnostics"], Is.Empty);
            Assert.That(File.ReadAllText(Path.Combine(_root, "playserv.schema.lock.json")), Is.EqualTo("unchanged"));
            Assert.That(File.ReadAllText(Path.Combine(_root, "playserv.schema.json")), Is.EqualTo("deliberately unrelated generation configuration"));
            Assert.That(Directory.GetFiles(_root).Select(Path.GetFileName).OrderBy(x => x),
                Is.EqualTo(new[] { "build-inputs.json", "Contract.cs", "playserv.schema.json", "playserv.schema.lock.json" }.OrderBy(x => x)));
        }

        [TestCase("Singleton = true", true)]
        [TestCase("Kind = PlayServSchemaKind.Part", true)]
        [TestCase("Kind = PlayServSchemaKind.Entity", false)]
        [TestCase("Kind = Rules.ContractKind", true)]
        [TestCase("Kind = (PlayServSchemaKind)2", true)]
        public void TypedReferencesUseExistingTargetRulesWithSourceDiagnostics(string targetOptions, bool fails)
        {
            var result = Validate("using Playserv.Schema; using Playserv.Data;\n" +
                "namespace Playserv.Data { public sealed class PlayServRecordRef<T> {} } public static class Rules { public const PlayServSchemaKind ContractKind = PlayServSchemaKind.Part; }\n" +
                "[PlayServSchema(\"target\", " + targetOptions + ")] public class Target {}\n" +
                "[PlayServSchema(\"owner\")] public class Owner { public PlayServRecordRef<Target> Target; }");
            Assert.That(result.ExitCode, Is.EqualTo(fails ? 1 : 0), result.Output);
            if (!fails) return;
            var diagnostic = JObject.Parse(result.Output)["diagnostics"].Single();
            Assert.That((string)diagnostic["code"], Is.EqualTo("PSSCHEMA004"));
            Assert.That((string)diagnostic["member"], Is.EqualTo("Target"));
            Assert.That((int)diagnostic["line"], Is.EqualTo(4));
        }

        [TestCase("System.Collections.Generic.Dictionary<string, PlayServRecordRef<Target>>")]
        [TestCase("System.Collections.Generic.List<System.Collections.Generic.List<PlayServRecordRef<Target>>>")]
        public void NestedTypedReferencesUseTheSameRulesAsGeneration(string fieldType)
        {
            var result = Validate("using Playserv.Schema; using Playserv.Data;\n" +
                "namespace Playserv.Data { public sealed class PlayServRecordRef<T> {} }\n" +
                "[PlayServSchema(\"target\", Singleton = true)] public class Target {}\n" +
                "[PlayServSchema(\"owner\")] public class Owner { public " + fieldType + " Targets; }");
            Assert.That(result.ExitCode, Is.EqualTo(1), result.Output);
            Assert.That((string)JObject.Parse(result.Output)["diagnostics"].Single()["code"], Is.EqualTo("PSSCHEMA004"));
        }

        [Test]
        public void SyntaxErrorsReportSourceLocation()
        {
            var result = Validate("using Playserv.Schema;\n[PlayServSchema] public class Invalid { public int Score = ; }");
            Assert.That(result.ExitCode, Is.EqualTo(1), result.Output);
            Assert.That(JObject.Parse(result.Output)["diagnostics"].Any(d => (string)d["code"] == "PSSCHEMA002" && (int)d["line"] == 2), Is.True);
        }

        [Test]
        public void PositionalRecordWithExplicitPropertyIsOneField()
        {
            var result = Validate("using Playserv.Schema;\nnamespace System.Runtime.CompilerServices { public class IsExternalInit {} }\n" +
                "[PlayServSchema(\"record\")] public record Profile(int Score) { public int Score { get; init; } = Score; }");
            Assert.That(result.ExitCode, Is.Zero, result.Output);
            Assert.That((int)JObject.Parse(result.Output)["schemaCount"], Is.EqualTo(1));
        }

        [Test]
        public void PositionalRecordWithExplicitPropertyInAnotherPartialFileIsOneField()
        {
            var result = Validate("using Playserv.Schema;\nnamespace System.Runtime.CompilerServices { public class IsExternalInit {} }\n" +
                "[PlayServSchema(\"record\")] public partial record Profile(int Score);",
                "public partial record Profile { public int Score { get; init; } }");
            Assert.That(result.ExitCode, Is.Zero, result.Output);
        }

        [Test]
        public void UnresolvedAttributeConstantCannotSilentlyUseMemberName()
        {
            var result = Validate("using Playserv.Schema; [PlayServSchema(\"owner\")] public class Owner { [PlayServField(Missing.Constant)] public int Field; }");
            Assert.That(result.ExitCode, Is.EqualTo(1), result.Output);
            Assert.That(JObject.Parse(result.Output)["diagnostics"].Any(d => (string)d["code"] == "PSSCHEMA005"), Is.True);
        }

        [Test]
        public void MissingExecutableFailsValidation()
        {
            var result = PlayServSchemaToolRunner.RunContractValidation(Path.Combine(_root, "missing-dotnet"), "tool.dll", _root, "input.json");
            Assert.That(result.Success, Is.False);
            Assert.That(result.Response, Is.Null);
            Assert.That(result.Message, Is.Not.Empty);
        }

        [Test]
        public void RealToolTimeoutFailsValidationAndStopsWaiting()
        {
            var package = PackageInfo.FindForAssetPath("Packages/com.playserv.schema-tool/package.json");
            Assert.That(package, Is.Not.Null);
            var elapsed = Stopwatch.StartNew();
            var result = PlayServSchemaToolRunner.RunContractValidation(PlayServSchemaToolRunner.ResolveBundledDotNet(),
                Path.Combine(package.resolvedPath, PlayServSchemaToolPackage.ToolRelativePath), _root, "missing-input.json", 0);
            Assert.That(result.Success, Is.False);
            StringAssert.Contains("timed out", result.Message);
            Assert.That(elapsed.Elapsed.TotalSeconds, Is.LessThan(5));
        }

        private Result Validate(string source, string other = null)
        {
            var files = new[] { Path.Combine(_root, "Contract.cs"), Path.Combine(_root, "Other.cs") };
            File.WriteAllText(files[0], source);
            if (other != null) File.WriteAllText(files[1], other);
            var input = Path.Combine(_root, "build-inputs.json");
            File.WriteAllText(input, JsonConvert.SerializeObject(new
            {
                version = 1,
                assemblies = new[] { new { name = "Game", sources = other == null ? files.Take(1).ToArray() : files,
                    defines = new[] { "UNITY_SERVER" }, references = new[] { typeof(PlayServSchemaAttribute).Assembly.Location,
                        typeof(object).Assembly.Location, typeof(System.Attribute).Assembly.Location }
                        .Concat(CompilationPipeline.GetAssemblies(AssembliesType.Editor).First(assembly => assembly.name == "Playserv.Tests.Editor").compiledAssemblyReferences)
                        .Distinct().ToArray() } }
            }));
            var package = PackageInfo.FindForAssetPath("Packages/com.playserv.schema-tool/package.json");
            Assert.That(package, Is.Not.Null, "Contract build tests require the shipped Schema Tool companion.");
            var start = new ProcessStartInfo(PlayServSchemaToolRunner.ResolveBundledDotNet(),
                "\"" + Path.Combine(package.resolvedPath, PlayServSchemaToolPackage.ToolRelativePath) +
                "\" validate-contracts --project \"" + _root + "\" --build-inputs \"" + input + "\" --json")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            start.EnvironmentVariables.Remove("PLAYSERV_SERVER_KEY");
            start.EnvironmentVariables.Remove("PLAYSERV_API_KEY");
            using (var process = Process.Start(start))
            {
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(30000)) { process.Kill(); Assert.Fail("Schema validation timed out."); }
                Task.WaitAll(output, error);
                return new Result { ExitCode = process.ExitCode, Output = output.Result + error.Result };
            }
        }

        private sealed class Result
        {
            public int ExitCode;
            public string Output;
        }
    }
}
