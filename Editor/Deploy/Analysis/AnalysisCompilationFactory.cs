using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Playserv.Deploy.Editor.Analysis
{
    internal static class AnalysisCompilationFactory
    {
        public static CSharpCompilation Create(IEnumerable<SyntaxTree> syntaxTrees)
        {
            if (syntaxTrees == null)
                throw new ArgumentNullException(nameof(syntaxTrees));

            return CSharpCompilation.Create(
                "UnityProject",
                syntaxTrees,
                BuildMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        }

        private static List<MetadataReference> BuildMetadataReferences()
        {
            var references = new List<MetadataReference>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            AddMetadataReference(references, seenPaths, typeof(object).Assembly.Location);
            AddMetadataReference(references, seenPaths, typeof(Enumerable).Assembly.Location);
            AddMetadataReference(references, seenPaths, typeof(List<>).Assembly.Location);
            AddMetadataReference(references, seenPaths, typeof(System.Threading.Tasks.Task).Assembly.Location);

            return references;
        }

        private static void AddMetadataReference(
            List<MetadataReference> references,
            HashSet<string> seenPaths,
            string assemblyLocation)
        {
            if (string.IsNullOrWhiteSpace(assemblyLocation))
                return;

            if (!File.Exists(assemblyLocation))
                return;

            if (!seenPaths.Add(assemblyLocation))
                return;

            references.Add(MetadataReference.CreateFromFile(assemblyLocation));
        }
    }
}
