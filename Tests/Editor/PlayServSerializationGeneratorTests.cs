using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Playserv.Editor;

namespace Playserv.Tests.Editor
{
    public sealed class PlayServSerializationGeneratorTests
    {
        [Test]
        public void CheckedOutCrLfFilesAreFreshAndNotRewritten()
        {
            var root = Path.Combine(Path.GetTempPath(), "playserv-serialization-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var output = new PlayServSerializationGenerator.Result();
                var relative = PlayServSerializationGenerator.Root + "Fixture/PlayServSerialization.g.cs";
                output.Files.Add(relative, "// Generated fixture\nclass Fixture {}\n");
                PlayServSerializationGenerator.Write(output, root);
                var path = Path.Combine(root, relative);
                var checkedOut = output.Files[relative].Replace("\n", "\r\n");
                File.WriteAllText(path, checkedOut);
                Assert.DoesNotThrow(() => PlayServSerializationGenerator.ValidateOutput(output, root));
                PlayServSerializationGenerator.Write(output, root);
                Assert.That(File.ReadAllText(path), Is.EqualTo(checkedOut), "A line-ending-only difference must not cause an asset rewrite.");
            }
            finally { Directory.Delete(root, true); }
        }

        [TestCase("public class Dto { public int Value; }")]
        [TestCase("public class PlayServGeneratedSerializationAttribute : System.Attribute {}")]
        [TestCase("// [PlayServGeneratedSerialization]\npublic class Dto { public string Value = \"PlayServGeneratedSerialization\"; }")]
        [TestCase("#if DISABLED\n[Playserv.Serialization.PlayServGeneratedSerialization] public class Dto {}\n#endif")]
        public void ProjectsWithoutOptInDoNotOpenMetadataReferences(string declaration)
        {
            var source = Path.GetTempFileName();
            try
            {
                File.WriteAllText(source, declaration);
                var result = PlayServSerializationGenerator.Build(new PlayServContractBuildInputs { assemblies = new[] {
                    new PlayServContractBuildAssembly { name = "UnmarkedFixture", sources = new[] { source }, references = new[] { source + ".missing.dll" } }
                } });
                Assert.That(result.Files, Is.Empty);
                Assert.That(result.Diagnostics, Is.Empty);
            }
            finally { File.Delete(source); }
        }

        [TestCase("using Marker = Playserv.Serialization.PlayServGeneratedSerializationAttribute; [Marker]")]
        [TestCase("[Playserv.Serialization.PlayServGeneratedSerializ\\u0061tion]")]
        [TestCase("[Playserv.Serialization.PlayServGeneratedSerializa\u200Dtion]")]
        public void AliasedAndUnicodeMarkersStillGenerateContracts(string declaration)
        {
            var source = Path.GetTempFileName();
            try
            {
                File.WriteAllText(source, declaration + " public class Dto { public int Value; }");
                var result = PlayServSerializationGenerator.Build(new PlayServContractBuildInputs { assemblies = new[] {
                    new PlayServContractBuildAssembly { name = "MarkerFixture", sources = new[] { source }, references = References() }
                } });
                Assert.That(result.TypeCount, Is.EqualTo(1));
                Assert.That(result.Files.Keys.Any(path => path.EndsWith(".g.cs")), Is.True);
            }
            finally { File.Delete(source); }
        }

        [Test]
        public void PreprocessWithoutAReportUsesActiveBuildOptions()
        {
            Assert.DoesNotThrow(() => new PlayServSerializationBuildProcessor().OnPreprocessBuild(null));
        }

        [Test]
        public void ChangedOrRemovedContractsCannotUseStaleGeneratedFiles()
        {
            var root = Path.Combine(Path.GetTempPath(), "playserv-serialization-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var inputs = new PlayServContractBuildInputs();
                var output = new PlayServSerializationGenerator.Result();
                var path = PlayServSerializationGenerator.Root + "Former/PlayServSerialization.g.cs";
                output.Files.Add(path, "stale fixture");
                PlayServSerializationGenerator.Write(output, root);
                Assert.Throws<UnityEditor.Build.BuildFailedException>(() => PlayServSerializationGenerator.Validate(inputs, root));
                PlayServSerializationGenerator.Write(PlayServSerializationGenerator.Build(inputs), root);
                Assert.DoesNotThrow(() => PlayServSerializationGenerator.Validate(inputs, root));
                Assert.That(File.Exists(Path.Combine(root, path)), Is.False);
            }
            finally { Directory.Delete(root, true); }
        }

        [Test]
        public void PlayerOnlyMetadataDoesNotReferenceMissingTypesInTheEditor()
        {
            var source = Path.GetTempFileName();
            try
            {
                File.WriteAllText(source, "using Playserv.Serialization;\n#if PLAYER_ONLY\n[PlayServGeneratedSerialization] public class OnlyPlayer { public int Value; }\n#endif");
                var references = References();
                PlayServContractBuildInputs Input(string[] defines) => new PlayServContractBuildInputs { assemblies = new[] {
                    new PlayServContractBuildAssembly { name = "ConditionalFixture", sources = new[] { source }, references = references, defines = defines }
                } };
                var method = Generator.GetMethod("BuildVariants", BindingFlags.NonPublic | BindingFlags.Static);
                Assert.That(method, Is.Not.Null, "Player metadata must be isolated from the actual Editor compilation context.");
                var result = (PlayServSerializationGenerator.Result)method.Invoke(null, new object[] { Input(new[] { "PLAYER_ONLY" }), Input(new[] { "UNITY_EDITOR" }) });
                var text = result.Files.Single(x => x.Key.EndsWith(".cs")).Value;
                StringAssert.StartsWith("#if UNITY_EDITOR\n", text);
                Assert.That(text.IndexOf("#else", StringComparison.Ordinal), Is.LessThan(text.IndexOf("global::OnlyPlayer", StringComparison.Ordinal)));
            }
            finally { File.Delete(source); }
        }

        private static string[] References() => UnityEditor.Compilation.CompilationPipeline.GetAssemblies()
            .First(a => a.name == "Playserv.Editor.ModelSync").compiledAssemblyReferences
            .Concat(new[] { typeof(Playserv.Schema.PlayServSchemaAttribute).Assembly.Location,
                AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Playserv.Runtime.Serialization").Location })
            .Distinct().ToArray();
        private static Type Generator => typeof(PlayServContractBuildValidator).Assembly.GetType("Playserv.Editor.PlayServSerializationGenerator");

        [TestCase("public class Dto : System.Collections.Generic.IEnumerable<int> { public int Value; public System.Collections.Generic.IEnumerator<int> GetEnumerator() { yield return 7; } System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator(); }")]
        [TestCase("[System.ComponentModel.TypeConverter(typeof(System.ComponentModel.StringConverter))] public class Dto { public int Value; }")]
        [TestCase("public class Dto { internal Dto() {} public Dto(int value) { Value = value + 1; } public int Value { get; set; } }")]
        [TestCase("public class Dto { public string foo; public string Foo; }")]
        [TestCase("public class Dto { [System.ComponentModel.DefaultValue(7)] public int Value = 7; }")]
        [TestCase("public class Dto { [System.Runtime.CompilerServices.CompilerGenerated] public int Hidden = 7; public int Visible = 2; }")]
        public void ContractsWhoseNewtonsoftSemanticsDifferFallBackEntirely(string declaration)
        {
            var path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, "using Playserv.Serialization; [PlayServGeneratedSerialization] " + declaration);
                var result = PlayServSerializationGenerator.Build(new PlayServContractBuildInputs { assemblies = new[] {
                    new PlayServContractBuildAssembly { name = "FallbackFixture", sources = new[] { path }, references = References() }
                } });
                Assert.That(result.TypeCount, Is.Zero, "Preserve Newtonsoft contract kind, constructor choice and case-insensitive matching.");
                Assert.That(result.Diagnostics, Is.Not.Empty);
            }
            finally { File.Delete(path); }
        }

        [Test, Timeout(30000)]
        public void ExpandingGenericDataGraphDoesNotRecurseForeverDuringLinkerGeneration()
        {
            var path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, "using Playserv.Serialization; using System.Collections.Generic; [PlayServGeneratedSerialization] public class Root { public Chain<int> Value; } public class Chain<T> { public Chain<List<T>> Next; }");
                var result = PlayServSerializationGenerator.Build(new PlayServContractBuildInputs { assemblies = new[] {
                    new PlayServContractBuildAssembly { name = "RecursiveFixture", sources = new[] { path }, references = References() }
                } });
                StringAssert.Contains("Chain`1", result.Files[PlayServSerializationGenerator.Root + "link.xml"]);
            }
            finally { File.Delete(path); }
        }

        [Test]
        public void RuntimeMetadataGenerationHasAnOfflineEditorEntryPoint()
        {
            var generator = typeof(PlayServContractBuildValidator).Assembly.GetType("Playserv.Editor.PlayServSerializationGenerator");
            Assert.That(generator, Is.Not.Null,
                "Opted-in contracts need deterministic C# serialization descriptors and registration.");
            Assert.That(generator.GetMethod("Generate"), Is.Not.Null);
        }

        [Test]
        public void GenerationIsDeterministicNamesArePreservedAndUnsupportedTypesFallBackWhole()
        {
            const string source = @"
using Playserv.Serialization;
using Playserv.Schema;
using Newtonsoft.Json;
namespace Customer {
 [PlayServGeneratedSerialization] public class Entity {
  [JsonProperty(""json"")][PlayServField(""schema"")][PlayServJsonName(""final"")] public string Value;
  [PlayServIgnore] public string Local;
  public int Count { get; set; }
 }
 [PlayServGeneratedSerialization] public class Unsupported { public string ReadOnly => ""preserve me""; public int Visible; }
 [PlayServGeneratedSerialization] public class UnsupportedPrivate {
  [JsonExtensionData] private System.Collections.Generic.Dictionary<string, Newtonsoft.Json.Linq.JToken> Extra;
  public int Visible;
 }
 [PlayServGeneratedSerialization] public class Sequence : System.Collections.Generic.IEnumerable<int> {
  public int Value;
  public System.Collections.Generic.IEnumerator<int> GetEnumerator() { yield return 7; }
  System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
 }
 [PlayServGeneratedSerialization, System.ComponentModel.TypeConverter(typeof(System.ComponentModel.StringConverter))]
 public class Converted { public int Value; }
 [PlayServGeneratedSerialization] public class ConstructorChoice {
  internal ConstructorChoice() { Value = -1; }
  public ConstructorChoice(int value) { Value = value + 1; }
  public int Value { get; set; }
 }
#if PLAYER_ONLY
 [PlayServGeneratedSerialization] public class Conditional { public int Active; }
#endif
} ";
            var path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, source);
                var references = References();
                var inputs = new PlayServContractBuildInputs { assemblies = new[] { new PlayServContractBuildAssembly
                {
                    name = "Customer", sources = new[] { path }, defines = new[] { "PLAYER_ONLY" }, references = references
                } } };
                Assert.That(Generator, Is.Not.Null);
                var method = Generator.GetMethod("Build", BindingFlags.Static | BindingFlags.NonPublic);
                Assert.That(method, Is.Not.Null);
                var first = method.Invoke(null, new object[] { inputs });
                var second = method.Invoke(null, new object[] { inputs });
                var files = (Dictionary<string, string>)first.GetType().GetField("Files").GetValue(first);
                var again = (Dictionary<string, string>)second.GetType().GetField("Files").GetValue(second);
                CollectionAssert.AreEquivalent(files, again);
                var generated = string.Join("\n", files.Where(x => x.Key.EndsWith(".cs")).Select(x => x.Value));
                StringAssert.Contains("global::Customer.Entity", generated);
                StringAssert.Contains("global::Customer.Conditional", generated);
                StringAssert.Contains("\"final\"", generated);
                StringAssert.Contains("Ignore<", generated);
                StringAssert.DoesNotContain("Register<global::Customer.Unsupported>", generated);
                StringAssert.DoesNotContain("Register<global::Customer.UnsupportedPrivate>", generated);
                StringAssert.DoesNotContain("Register<global::Customer.Sequence>", generated);
                StringAssert.DoesNotContain("Register<global::Customer.Converted>", generated);
                StringAssert.DoesNotContain("Register<global::Customer.ConstructorChoice>", generated);
                Assert.That(files.ContainsKey(PlayServSerializationGenerator.Root + "link.xml"), Is.True,
                    "Opted-in fallback DTOs must retain their private serialization members in IL2CPP.");
                StringAssert.Contains("Customer.UnsupportedPrivate", files[PlayServSerializationGenerator.Root + "link.xml"]);
                var diagnostics = (List<string>)first.GetType().GetField("Diagnostics").GetValue(first);
                Assert.That(diagnostics.Any(x => x.Contains("Unsupported") && x.Contains("fallback")), Is.True);
            }
            finally { File.Delete(path); }
        }

        [Test]
        public void SafeContractInAnUnsafeAssemblyCanStillGenerate()
        {
            var path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, "using Playserv.Serialization; [PlayServGeneratedSerialization] public class Safe { public int Value; } public static class UnsafeHelper { public unsafe static int Read(int* p) => *p; }");
                var inputs = new PlayServContractBuildInputs { assemblies = new[] { new PlayServContractBuildAssembly {
                    name = "UnsafeAssembly", sources = new[] { path }, references = References()
                } } };
                var result = PlayServSerializationGenerator.Build(inputs);
                Assert.That(result.TypeCount, Is.EqualTo(1));
            }
            finally { File.Delete(path); }
        }
    }
}
