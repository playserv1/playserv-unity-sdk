using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;

namespace Playserv.Tests.Editor
{
    public sealed class PlayServIl2CppBuildSmokeTests
    {
        private const string RunEnvironmentVariable = "PLAYSERV_RUN_IL2CPP_TESTS";
        private const string TestRootAssetPath = "Assets/PlayServIl2CppTestArtifacts";
        private const string TestSceneAssetPath = TestRootAssetPath + "/Smoke.unity";

        [Test]
        [Category("IL2CPP")]
        [Timeout(1800000)]
        public void ActiveTarget_BuildsWithIl2Cpp()
        {
            if (!string.Equals(
                    Environment.GetEnvironmentVariable(RunEnvironmentVariable),
                    "1",
                    StringComparison.Ordinal))
            {
                Assert.Ignore($"Set {RunEnvironmentVariable}=1 to run the IL2CPP build smoke test.");
            }

            var target = EditorUserBuildSettings.activeBuildTarget;
            var targetGroup = BuildPipeline.GetBuildTargetGroup(target);
            if (targetGroup == BuildTargetGroup.Unknown)
                Assert.Fail($"Active build target {target} has no build target group.");

            var previousBackend = PlayerSettings.GetScriptingBackend(targetGroup);
            try
            {
                EnsureTestScene();
                PlayerSettings.SetScriptingBackend(targetGroup, ScriptingImplementation.IL2CPP);

                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { TestSceneAssetPath },
                    target = target,
                    locationPathName = GetBuildLocation(target),
                    options = BuildOptions.Development
                });

                Assert.That(
                    report.summary.result,
                    Is.EqualTo(BuildResult.Succeeded),
                    $"IL2CPP build failed with {report.summary.totalErrors} errors.");

                AssertRuntimeRegistrationSurvivedLinking(
                    "Playserv.Runtime.Transport.WebSocket",
                    "WebSocketTransportModuleRegistration");
            }
            finally
            {
                PlayerSettings.SetScriptingBackend(targetGroup, previousBackend);
                AssetDatabase.DeleteAsset(TestRootAssetPath);
            }
        }

        private static void EnsureTestScene()
        {
            if (!AssetDatabase.IsValidFolder(TestRootAssetPath))
                AssetDatabase.CreateFolder("Assets", "PlayServIl2CppTestArtifacts");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Assert.That(EditorSceneManager.SaveScene(scene, TestSceneAssetPath), Is.True);
        }

        private static string GetBuildLocation(BuildTarget target)
        {
            var projectRoot = Directory.GetParent(UnityEngine.Application.dataPath)?.FullName ??
                              UnityEngine.Application.dataPath;
            var outputRoot = Path.Combine(projectRoot, "Temp", "PlayServTests", "Il2Cpp");
            Directory.CreateDirectory(outputRoot);

            switch (target)
            {
                case BuildTarget.StandaloneOSX:
                    return Path.Combine(outputRoot, "PlayServTests.app");
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                    return Path.Combine(outputRoot, "PlayServTests.exe");
                default:
                    return Path.Combine(outputRoot, "PlayServTests");
            }
        }

        private static void AssertRuntimeRegistrationSurvivedLinking(
            string assemblyName,
            string registrationTypeName)
        {
            var projectRoot = Directory.GetParent(UnityEngine.Application.dataPath)?.FullName ??
                              UnityEngine.Application.dataPath;
            var libraryRoot = Path.Combine(projectRoot, "Library");
            var generatedSource = Directory
                .EnumerateFiles(
                    libraryRoot,
                    assemblyName + ".cpp",
                    SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();

            Assert.That(
                generatedSource,
                Is.Not.Null,
                $"UnityLinker removed {assemblyName} before IL2CPP conversion.");
            StringAssert.Contains(
                registrationTypeName,
                File.ReadAllText(generatedSource),
                $"IL2CPP output for {assemblyName} does not contain its runtime registration.");
        }
    }
}
