using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        public void PreservationAppliesAcrossOneAssemblyButDoesNotLeakIntoANestedAssembly()
        {
            var root = Path.Combine(Path.GetTempPath(), "playserv-linker-scope-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                File.WriteAllText(Path.Combine(root, "Parent.asmdef"), "{\"name\":\"Parent\"}");
                var annotation = Path.Combine(root, "AssemblyInfo.cs");
                var initializer = Path.Combine(root, "Registration.cs");
                File.WriteAllText(annotation, AlwaysLinkAssemblyAttribute);
                File.WriteAllText(initializer, "class Registration { [UnityEngine.RuntimeInitializeOnLoadMethod] static void Boot() {} }");
                Assert.That(FindUnpreservedAssemblies(new[] { annotation, initializer }), Is.Empty);
                var nested = Path.Combine(root, "Nested");
                Directory.CreateDirectory(nested);
                var definition = Path.Combine(nested, "Child.asmdef");
                File.WriteAllText(definition, "{\"name\":\"Child\"}");
                var child = Path.Combine(nested, "ChildRegistration.cs");
                File.WriteAllText(child, File.ReadAllText(initializer));
                Assert.That(FindUnpreservedAssemblies(new[] { annotation, initializer, child }), Is.EqualTo(new[] { definition }));
            }
            finally { Directory.Delete(root, true); }
        }

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
            var allSources = new List<string>();

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
                    allSources.Add(sourcePath);
                    if (!source.Contains(RuntimeInitializerAttribute))
                        continue;

                    initializerSources.Add(sourcePath);
                }
            }

            Assert.That(initializerSources, Is.Not.Empty,
                "Expected the SDK to contain runtime initializer assemblies.");
            var missingAlwaysLink = FindUnpreservedAssemblies(allSources);
            Assert.That(
                missingAlwaysLink,
                Is.Empty,
                "Assemblies reached only through RuntimeInitializeOnLoadMethod can be removed " +
                "from IL2CPP builds unless their owning assembly declares " +
                "[assembly: UnityEngine.Scripting.AlwaysLinkAssembly]. Missing:\n" +
                string.Join("\n", missingAlwaysLink));
        }

        private static string[] FindUnpreservedAssemblies(IEnumerable<string> sources) => sources.GroupBy(AssemblyDefinition)
            .Where(group => group.Any(path => File.ReadAllText(path).Contains(RuntimeInitializerAttribute)) &&
                            !group.Any(path => File.ReadAllText(path).Contains(AlwaysLinkAssemblyAttribute)))
            .Select(group => group.Key).ToArray();

        private static string AssemblyDefinition(string source)
        {
            for (var directory = new DirectoryInfo(Path.GetDirectoryName(source)); directory != null; directory = directory.Parent)
            {
                var definition = directory.GetFiles("*.asmdef").FirstOrDefault();
                if (definition != null) return definition.FullName;
            }
            return source;
        }
    }
}
