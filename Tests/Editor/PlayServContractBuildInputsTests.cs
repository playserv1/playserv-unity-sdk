using System;
using System.IO;
using NUnit.Framework;
using Playserv.Editor;
using Playserv.Schema;
using UnityEditor;

namespace Playserv.Tests.Editor
{
    public sealed class PlayServContractBuildInputsTests
    {
        [TestCase("// [PlayServSchema] public class Fake {}\npublic class Real {}", false)]
        [TestCase("namespace Other { class PlayServSchemaAttribute : System.Attribute {} [PlayServSchema] class Fake {} }", false)]
        [TestCase("using S = Playserv.Schema.PlayServSchemaAttribute; [S(\"contract\")] public class Real {}", true)]
        [TestCase("using Playserv.Schema;\n#if UNITY_SERVER\n[PlayServSchema] public class Real {}\n#endif", true)]
        [TestCase("using Playserv.Schema;\n#if UNITY_EDITOR\n[PlayServSchema] public class Fake {}\n#endif", false)]
        public void DiscoveryUsesActualAttributesAndBuildSymbols(string source, bool expected)
        {
            var path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, source);
                var inputs = new PlayServContractBuildInputs { assemblies = new[] { new PlayServContractBuildAssembly
                {
                    name = "Discovery", sources = new[] { path }, defines = new[] { "UNITY_SERVER" },
                    references = new[] { typeof(object).Assembly.Location, typeof(PlayServSchemaAttribute).Assembly.Location }
                } } };
                Assert.That(inputs.ContainsContracts(), Is.EqualTo(expected));
            }
            finally { File.Delete(path); }
        }

        [Test]
        public void BuildInputsExcludeEditorAndTestAssemblies()
        {
            var inputs = PlayServContractBuildInputs.Collect(new BuildPlayerOptions { target = EditorUserBuildSettings.activeBuildTarget });
            Assert.That(Array.Exists(inputs.assemblies, assembly => assembly.name == "Playserv.Tests.Editor" || assembly.name == "Playserv.Editor.ModelSync"), Is.False);
            Assert.That(Array.Exists(inputs.assemblies, assembly => Array.IndexOf(assembly.defines, "UNITY_EDITOR") >= 0), Is.False);
        }

        [Test]
        public void BuildInputsHonourPositiveAndNegatedAssemblyConstraints()
        {
            if (!File.Exists("Assets/ContractValidation/Constrained/ConstrainedContract.cs")) Assert.Ignore("SDK CI fixture required.");
            var inputs = PlayServContractBuildInputs.Collect(new BuildPlayerOptions
            {
                target = EditorUserBuildSettings.activeBuildTarget, extraScriptingDefines = new[] { "PLAYSERV_CONTRACT_CONSTRAINED" }
            });
            Assert.That(Array.Exists(inputs.assemblies, assembly => assembly.name == "Playserv.ConstrainedContractFixture"), Is.True);
            Assert.That(Array.Exists(inputs.assemblies, assembly => assembly.name == "Playserv.ExcludedContractFixture"), Is.False);
        }

        [TestCase(BuildTarget.WebGL, 1, BuildOptions.None, false, false)]
        [TestCase(BuildTarget.StandaloneWindows64, 1, BuildOptions.None, true, false)]
        [TestCase(BuildTarget.StandaloneWindows64, 0, BuildOptions.Development, false, true)]
        public void BuildDefinesFollowThisBuildInsteadOfRememberedEditorSettings(BuildTarget target, int subtarget,
            BuildOptions options, bool server, bool development)
        {
            var defines = PlayServContractBuildInputs.BuildDefines(new[] { "UNITY_EDITOR", "UNITY_SERVER", "DEVELOPMENT_BUILD", "UNITY_INCLUDE_TESTS" },
                new BuildPlayerOptions { target = target, subtarget = subtarget, options = options });
            Assert.That(Array.IndexOf(defines, "UNITY_SERVER") >= 0, Is.EqualTo(server));
            Assert.That(Array.IndexOf(defines, "DEVELOPMENT_BUILD") >= 0, Is.EqualTo(development));
            Assert.That(defines, Does.Not.Contain("UNITY_INCLUDE_TESTS"));
        }

        [TestCase("{\"success\":false,\"command\":\"validate-contracts\",\"protocolVersion\":1,\"diagnostics\":[null]}", 1)]
        [TestCase("{\"success\":true,\"command\":\"validate-contracts\",\"protocolVersion\":1,\"diagnostics\":[{}]}", 0)]
        [TestCase("{\"success\":true,\"command\":\"validate-contracts\",\"protocolVersion\":1,\"diagnostics\":[{\"severity\":\"mystery\"}]}", 0)]
        [TestCase("{}", 0)]
        [TestCase("garbage", 0)]
        public void MalformedToolResponseCannotPassOrLeakNullDiagnostics(string json, int exitCode)
        {
            var result = PlayServSchemaToolRunner.ParseContractValidationResult(exitCode, json, "");
            Assert.That(result.Success, Is.False);
            Assert.That(result.Response, Is.Null);
            StringAssert.Contains("response", result.Message);
        }
    }
}
