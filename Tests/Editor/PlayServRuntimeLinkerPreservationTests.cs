using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Playserv.Tests.Editor
{
    public sealed class PlayServRuntimeLinkerPreservationTests
    {
        private const string RuntimeInitializerAttribute = "RuntimeInitializeOnLoadMethod";
        private const string AlwaysLinkAssemblyAttribute =
            "[assembly: UnityEngine.Scripting.AlwaysLinkAssembly]";

        [Test]
        public void RuntimeInitializerAssemblies_AreAlwaysProcessedByUnityLinker()
        {
            var package = PackageInfo.FindForAssetPath("Packages/com.playserv.sdk/package.json");
            Assert.That(package, Is.Not.Null, "Could not resolve the PlayServ SDK package root.");

            var searchRoots = new[]
            {
                Path.Combine(package.resolvedPath, "Runtime"),
                Path.Combine(package.resolvedPath, "CompanionPackages~")
            };
            var initializerSources = new List<string>();
            var missingAlwaysLink = new List<string>();

            foreach (var searchRoot in searchRoots)
            {
                if (!Directory.Exists(searchRoot))
                    continue;

                foreach (var sourcePath in Directory.EnumerateFiles(
                             searchRoot,
                             "*.cs",
                             SearchOption.AllDirectories))
                {
                    var source = File.ReadAllText(sourcePath);
                    if (!source.Contains(RuntimeInitializerAttribute))
                        continue;

                    initializerSources.Add(sourcePath);
                    if (!source.Contains(AlwaysLinkAssemblyAttribute))
                        missingAlwaysLink.Add(sourcePath);
                }
            }

            Assert.That(initializerSources, Is.Not.Empty,
                "Expected the SDK to contain runtime initializer assemblies.");
            Assert.That(
                missingAlwaysLink,
                Is.Empty,
                "Assemblies reached only through RuntimeInitializeOnLoadMethod can be removed " +
                "from IL2CPP builds unless their registration source declares " +
                "[assembly: UnityEngine.Scripting.AlwaysLinkAssembly]. Missing:\n" +
                string.Join("\n", missingAlwaysLink));
        }
    }
}
