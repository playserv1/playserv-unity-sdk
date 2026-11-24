using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.UnityLinker;

namespace Playserv.Editor
{
    internal sealed class PlayServSchemaAdvisoryLinker : BuildPlayerProcessor, IPreprocessBuildWithReport, IUnityLinkerProcessor
    {
        private static BuildPlayerOptions? _options;
        private static string _linkFile;
        public override int callbackOrder => -10000;

        public override void PrepareForBuild(BuildPlayerContext context) => _options = context.BuildPlayerOptions;

        public void OnPreprocessBuild(BuildReport report)
        {
            _linkFile = null;
            var options = _options ?? new BuildPlayerOptions
            {
                target = report == null ? EditorUserBuildSettings.activeBuildTarget : report.summary.platform,
                options = report == null ? BuildOptions.None : report.summary.options,
                subtarget = (int)EditorUserBuildSettings.standaloneBuildSubtarget
            };
            _options = null;
            var xml = CreateXml(PlayServContractBuildInputs.Collect(options));
            var path = Path.GetFullPath("Library/PlayServ/SchemaAdvisory/link.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, xml);
            _linkFile = path;
        }

        public string GenerateAdditionalLinkXmlFile(BuildReport report, UnityLinkerBuildPipelineData data) => _linkFile;
        public void OnBeforeRun(BuildReport report, UnityLinkerBuildPipelineData data) { }
        public void OnAfterRun(BuildReport report, UnityLinkerBuildPipelineData data) { }

        internal static string CreateXml(PlayServContractBuildInputs inputs)
        {
            var assemblies = new Dictionary<string, IAssemblySymbol>(StringComparer.Ordinal);
            foreach (var compilation in inputs.CreateCompilations())
            {
                assemblies[compilation.AssemblyName] = compilation.Assembly;
                foreach (var reference in compilation.References)
                    if (compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol assembly &&
                        !assemblies.ContainsKey(assembly.Name) && assembly.Modules.Any(module =>
                            module.ReferencedAssemblySymbols.Any(dependency => dependency.Name == "Playserv.Runtime")))
                        assemblies[assembly.Name] = assembly;
            }
            var linker = new XElement("linker");
            foreach (var assembly in assemblies.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                var types = Types(assembly.Value.GlobalNamespace).Where(IsEntity)
                    .Select(LinkName).Distinct().OrderBy(name => name, StringComparer.Ordinal).ToArray();
                if (types.Length == 0) continue;
                linker.Add(new XElement("assembly", new XAttribute("fullname", assembly.Key),
                    types.Select(name => new XElement("type", new XAttribute("fullname", name), new XAttribute("preserve", "all")))));
            }
            return linker.ToString();
        }

        private static IEnumerable<INamedTypeSymbol> Types(INamespaceOrTypeSymbol parent)
        {
            foreach (var type in parent.GetTypeMembers())
            {
                yield return type;
                foreach (var nested in Types(type)) yield return nested;
            }
            if (parent is INamespaceSymbol ns)
                foreach (var child in ns.GetNamespaceMembers())
                    foreach (var type in Types(child)) yield return type;
        }

        private static bool IsEntity(INamedTypeSymbol type)
        {
            for (var containing = type; containing != null; containing = containing.ContainingType)
                if (containing.Arity != 0) return false;
            if (type.TypeKind == TypeKind.Enum) return false;
            var attribute = type.GetAttributes().FirstOrDefault(value => value.AttributeClass?.ToDisplayString() == "Playserv.Schema.PlayServSchemaAttribute");
            if (attribute == null) return false;
            var kind = attribute.NamedArguments.FirstOrDefault(pair => pair.Key == "Kind").Value.Value;
            return kind == null ? type.TypeKind == TypeKind.Class : Convert.ToInt32(kind) == 1 ||
                Convert.ToInt32(kind) == 0 && type.TypeKind == TypeKind.Class;
        }

        private static string LinkName(INamedTypeSymbol type) => type.ContainingType != null
            ? LinkName(type.ContainingType) + "/" + type.MetadataName
            : (type.ContainingNamespace.IsGlobalNamespace ? "" : type.ContainingNamespace.ToDisplayString() + ".") + type.MetadataName;
    }
}
