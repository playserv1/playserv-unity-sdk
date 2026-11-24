using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Playserv.Editor;
using Playserv.Schema;

namespace Playserv.Tests.Editor
{
    public sealed class PlayServSchemaAdvisoryLinkerTests
    {
        [Test, Timeout(600000)]
        public void StrippedPlayerDiscoversUnreferencedEntityAndItsMetadata()
        {
            if (Environment.GetEnvironmentVariable("PLAYSERV_RUN_ADVISORY_STRIPPING_TESTS") != "1")
                Assert.Ignore("Set PLAYSERV_RUN_ADVISORY_STRIPPING_TESTS=1 for the standalone player stripping smoke test.");
            if (UnityEditor.EditorUserBuildSettings.activeBuildTarget != UnityEditor.BuildTarget.StandaloneWindows64)
                Assert.Ignore("This executable probe runs on a Windows standalone player.");
            Assert.That(File.Exists("Assets/SchemaAdvisorySmoke.cs"), Is.True);
            var setup = UnityEditor.SceneManagement.EditorSceneManager.GetSceneManagerSetup();
            var previous = UnityEditor.PlayerSettings.GetManagedStrippingLevel(UnityEditor.BuildTargetGroup.Standalone);
            const string scenePath = "Assets/PlayServAdvisorySmoke.unity";
            var output = Path.GetFullPath("Temp/PlayServAdvisorySmoke/" + Guid.NewGuid().ToString("N"));
            try
            {
                var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                    UnityEditor.SceneManagement.NewSceneMode.Single);
                UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, scenePath);
                UnityEditor.PlayerSettings.SetManagedStrippingLevel(UnityEditor.BuildTargetGroup.Standalone, UnityEditor.ManagedStrippingLevel.High);
                var report = UnityEditor.BuildPipeline.BuildPlayer(new UnityEditor.BuildPlayerOptions
                {
                    target = UnityEditor.BuildTarget.StandaloneWindows64, scenes = new[] { scenePath },
                    extraScriptingDefines = new[] { "PLAYSERV_ADVISORY_STRIPPING_SMOKE" },
                    locationPathName = Path.Combine(output, "Player.exe")
                });
                Assert.That(report.summary.result, Is.EqualTo(UnityEditor.Build.Reporting.BuildResult.Succeeded));
                var log = Path.Combine(output, "player.log");
                using var player = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = Path.Combine(output, "Player.exe"),
                    Arguments = "-batchmode -nographics -logFile \"" + log + "\"",
                    UseShellExecute = false, CreateNoWindow = true, WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                });
                if (!player.WaitForExit(60000)) { player.Kill(); Assert.Fail("Advisory player did not exit within 60 seconds."); }
                Assert.That(player.ExitCode, Is.Zero, File.Exists(log) ? File.ReadAllText(log) : "Player log missing.");
                StringAssert.Contains("PLAYSERV_ADVISORY_STRIPPING_OK", File.ReadAllText(log));
            }
            finally
            {
                UnityEditor.PlayerSettings.SetManagedStrippingLevel(UnityEditor.BuildTargetGroup.Standalone, previous);
                if (setup.Any(scene => scene.isLoaded)) UnityEditor.SceneManagement.EditorSceneManager.RestoreSceneManagerSetup(setup);
                else UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);
                UnityEditor.AssetDatabase.DeleteAsset(scenePath);
            }
        }

        [Test]
        public void AdvisoryLinkerPreservesOnlyActiveEntityContractsIncludingNestedTypes()
        {
            var source = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".cs");
            File.WriteAllText(source, @"
using Playserv.Schema;
namespace Customer {
 public class Outer { [PlayServSchema] public class Entity {} }
 [PlayServSchema(Kind=PlayServSchemaKind.Entity)] public struct ExplicitEntity {}
 [PlayServSchema] public struct Part {}
 [PlayServSchema] public enum Choice { A }
 [PlayServSchema] public class Generic<T> {}
#if PLAYER_ONLY
 [PlayServSchema] public class ConditionalEntity {}
#endif
}");
            try
            {
                var type = typeof(PlayServContractBuildValidator).Assembly.GetType("Playserv.Editor.PlayServSchemaAdvisoryLinker");
                Assert.That(type, Is.Not.Null, "Automatically discovered types must survive player stripping.");
                var inputs = new PlayServContractBuildInputs { assemblies = new[] { new PlayServContractBuildAssembly
                {
                    name = "Customer", sources = new[] { source }, defines = new[] { "PLAYER_ONLY" },
                    references = new[] { typeof(object).Assembly.Location, typeof(PlayServSchemaAttribute).Assembly.Location }
                        .Concat(UnityEditor.Compilation.CompilationPipeline.GetAssemblies().First(a => a.name == "Playserv.Tests.Editor").compiledAssemblyReferences)
                        .Distinct().ToArray()
                } } };
                var xml = (string)type.GetMethod("CreateXml", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { inputs });
                StringAssert.Contains("Customer.Outer/Entity", xml);
                StringAssert.Contains("Customer.ExplicitEntity", xml);
                StringAssert.Contains("Customer.ConditionalEntity", xml);
                StringAssert.DoesNotContain("Customer.Part", xml);
                StringAssert.DoesNotContain("Customer.Choice", xml);
                StringAssert.DoesNotContain("Customer.Generic", xml);
            }
            finally { File.Delete(source); }
        }
    }
}
