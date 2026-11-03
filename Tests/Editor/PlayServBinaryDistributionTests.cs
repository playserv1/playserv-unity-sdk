using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Playserv.Editor;
using UnityEditor.Compilation;

namespace Playserv.Tests.Editor
{
    public sealed class PlayServBinaryDistributionTests
    {
        [Test]
        public void GeneratedModuleAssemblyReferencesExistInInstalledPackage()
        {
            var definition = JObject.Parse(PlayServGeneratedCompatibilityLayer.GenerateProjectAssemblyDefinition());
            var assemblies = CompilationPipeline.GetAssemblies(AssembliesType.Editor).Select(a => a.name).ToArray();
            foreach (var reference in ((JArray)definition["references"]).Values<string>())
                Assert.That(assemblies, Does.Contain(reference), "Generated source references an unavailable assembly definition.");
        }

        [Test]
        public void ModuleValidationAcceptsTheInstalledCoreAssemblyFormat()
        {
            var report = PlayServModuleValidator.Validate();
            var assemblyErrors = report.Issues.Where(issue => issue.Severity == PlayServModuleValidationSeverity.Error);
            Assert.That(assemblyErrors.Select(issue => issue.ToLogLine()).ToArray(), Is.Empty);
        }
    }
}
