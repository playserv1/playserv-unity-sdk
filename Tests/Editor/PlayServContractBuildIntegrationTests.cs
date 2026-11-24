using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEngine.TestTools;

namespace Playserv.Tests.Editor
{
    public sealed class PlayServContractBuildIntegrationTests
    {
        [UnitySetUp]
        public IEnumerator WaitForBuildReadiness()
        {
            if (!File.Exists("Assets/ContractValidation/ContractBuildFixture.cs"))
                Assert.Ignore("This integration fixture is installed by the SDK CI consuming project.");

            CompilationPipeline.RequestScriptCompilation();
            AssetDatabase.Refresh();
            Assert.That(EditorApplication.isCompiling, Is.True,
                "The regression must start with a pending script compilation.");
            yield return new WaitForContractBuildReadiness();
        }

        [Test, Timeout(600000)]
        public void BuildPipelineRejectsInvalidContractsAndBuildsValidContracts()
        {
            if (!File.Exists("Assets/ContractValidation/ContractBuildFixture.cs"))
                Assert.Ignore("This integration fixture is installed by the SDK CI consuming project.");

            var setup = EditorSceneManager.GetSceneManagerSetup();
            const string scenePath = "Assets/PlayServContractBuildTest.unity";
            var outputRoot = Path.GetFullPath("Temp/PlayServContractBuildTest");
            var previousIgnore = LogAssert.ignoreFailingMessages;
            try
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, scenePath);
                var options = new BuildPlayerOptions
                {
                    scenes = new[] { scenePath }, target = EditorUserBuildSettings.activeBuildTarget,
                    locationPathName = Path.Combine(outputRoot, "Invalid", "Player.exe"),
                    extraScriptingDefines = new[] { "PLAYSERV_CONTRACT_INVALID" }
                };
                LogAssert.ignoreFailingMessages = true;
                AssertBuildReady();
                var invalid = BuildPipeline.BuildPlayer(options);
                Assert.That(invalid.summary.result, Is.EqualTo(BuildResult.Failed),
                    "A normal player build must reject duplicate serialized contract fields.");
                var messages = string.Join("\n", Array.ConvertAll(invalid.steps,
                    step => string.Join("\n", Array.ConvertAll(step.messages, message => message.content))));
                StringAssert.Contains("PSSCHEMA003", messages);
                StringAssert.Contains("DuplicateScore", messages);

                options.extraScriptingDefines = new[] { "PLAYSERV_CONTRACT_CONSTRAINED" };
                options.locationPathName = Path.Combine(outputRoot, "Constrained", "Player.exe");
                AssertBuildReady();
                var constrained = BuildPipeline.BuildPlayer(options);
                Assert.That(constrained.summary.result, Is.EqualTo(BuildResult.Failed),
                    "Contracts in an asmdef enabled only by extraScriptingDefines must also be validated.");
                var constrainedMessages = string.Join("\n", Array.ConvertAll(constrained.steps,
                    step => string.Join("\n", Array.ConvertAll(step.messages, message => message.content))));
                StringAssert.Contains("PSSCHEMA003", constrainedMessages);
                StringAssert.Contains("ConstrainedContract", constrainedMessages);

                options.extraScriptingDefines = new[] { "PLAYSERV_CONTRACT_IMPLICIT" };
                options.locationPathName = Path.Combine(outputRoot, "Implicit", "Player.exe");
                AssertBuildReady();
                var implicitReference = BuildPipeline.BuildPlayer(options);
                Assert.That(implicitReference.summary.result, Is.EqualTo(BuildResult.Failed),
                    "Assembly-CSharp must reference newly enabled autoReferenced assemblies during validation.");
                var implicitMessages = string.Join("\n", Array.ConvertAll(implicitReference.steps,
                    step => string.Join("\n", Array.ConvertAll(step.messages, message => message.content))));
                StringAssert.Contains("PSSCHEMA003", implicitMessages);
                StringAssert.Contains("ImplicitContractFixture", implicitMessages);

                LogAssert.ignoreFailingMessages = previousIgnore;
                options.extraScriptingDefines = Array.Empty<string>();
                options.locationPathName = Path.Combine(outputRoot, "Valid", "Player.exe");
                AssertBuildReady();
                var valid = BuildPipeline.BuildPlayer(options);
                Assert.That(valid.summary.result, Is.EqualTo(BuildResult.Succeeded));
            }
            finally
            {
                LogAssert.ignoreFailingMessages = previousIgnore;
                if (setup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(setup);
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                AssetDatabase.DeleteAsset(scenePath);
            }
        }

        private static void AssertBuildReady()
        {
            Assert.That(EditorApplication.isCompiling || EditorApplication.isUpdating, Is.False,
                "The contract build must wait for script compilation and asset import.");
        }
    }

    public sealed class PlayServContractBuildReadinessTests
    {
        [UnityTest]
        public IEnumerator PendingCompilationTimesOutInsteadOfStartingTheBuild()
        {
            CompilationPipeline.RequestScriptCompilation();
            AssetDatabase.Refresh();
            Assert.That(EditorApplication.isCompiling, Is.True);
            var wait = new WaitForContractBuildReadiness(0).Perform();
            try
            {
                Assert.That(wait.MoveNext(), Is.True);
                var error = Assert.Throws<TimeoutException>(() => wait.MoveNext());
                StringAssert.Contains("before the contract build", error.Message);
            }
            finally
            {
                (wait as IDisposable)?.Dispose();
            }
            yield return new WaitForContractBuildReadiness();
        }
    }

    internal sealed class WaitForContractBuildReadiness : IEditModeTestYieldInstruction
    {
        private readonly double _timeoutSeconds;
        public bool ExpectDomainReload => true;
        public bool ExpectedPlaymodeState => false;

        public WaitForContractBuildReadiness(double timeoutSeconds = 300)
        {
            _timeoutSeconds = timeoutSeconds;
        }

        public IEnumerator Perform()
        {
            // Let the test runner save its continuation before unlocking the domain.
            yield return null;
            var deadline = EditorApplication.timeSinceStartup + _timeoutSeconds;
            AssetDatabase.Refresh();
            EditorApplication.UnlockReloadAssemblies();
            try
            {
                while (EditorApplication.isCompiling || EditorApplication.isUpdating)
                {
                    if (EditorApplication.timeSinceStartup >= deadline)
                        throw new TimeoutException("Timed out waiting for script compilation and asset import before the contract build.");
                    yield return null;
                }
                if (EditorUtility.scriptCompilationFailed)
                    throw new InvalidOperationException("Script compilation failed before the contract build.");
            }
            finally
            {
                EditorApplication.LockReloadAssemblies();
            }
        }
    }
}
