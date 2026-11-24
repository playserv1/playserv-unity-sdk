using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Compilation;

namespace Playserv.Editor
{
    [Serializable]
    internal sealed class PlayServContractBuildInputs
    {
        public int version = 1;
        public PlayServContractBuildAssembly[] assemblies = Array.Empty<PlayServContractBuildAssembly>();

        internal static PlayServContractBuildInputs Collect(BuildPlayerOptions options)
        {
            if (options.target != EditorUserBuildSettings.activeBuildTarget)
                throw new BuildFailedException("PlayServ: switch the active build target before validating contracts (use -buildTarget in CI).");
            var inputs = new PlayServContractBuildInputs
            {
                assemblies = CompilationPipeline.GetAssemblies(AssembliesType.PlayerWithoutTestAssemblies)
                    .OrderBy(assembly => assembly.name, StringComparer.Ordinal)
                    .Select(assembly => new PlayServContractBuildAssembly
                    {
                        name = assembly.name,
                        sources = assembly.sourceFiles.Select(Path.GetFullPath).Distinct(StringComparer.Ordinal)
                            .OrderBy(path => path, StringComparer.Ordinal).ToArray(),
                        defines = BuildDefines(assembly.defines, options),
                        references = assembly.compiledAssemblyReferences.Select(ResolveReference)
                            .Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal).ToArray(),
                        assemblyReferences = assembly.assemblyReferences.Select(reference => reference.name).OrderBy(name => name, StringComparer.Ordinal).ToArray()
                    }).ToArray()
            };
            inputs.ApplyAssemblyDefinitions(options);
            return inputs;
        }

        internal static string[] BuildDefines(IEnumerable<string> defines, BuildPlayerOptions options)
        {
            var server = BuildPipeline.GetBuildTargetGroup(options.target) == BuildTargetGroup.Standalone &&
                options.subtarget == (int)StandaloneBuildSubtarget.Server;
            var development = (options.options & BuildOptions.Development) != 0;
            var assertions = development || (options.options & BuildOptions.ForceEnableAssertions) != 0;
            return defines.Where(define => define != "UNITY_EDITOR" && define != "UNITY_EDITOR_WIN" &&
                    define != "UNITY_EDITOR_OSX" && define != "UNITY_EDITOR_LINUX" && define != "UNITY_INCLUDE_TESTS" &&
                    define != "UNITY_SERVER" && define != "DEVELOPMENT_BUILD" && define != "UNITY_ASSERTIONS")
                .Concat(options.extraScriptingDefines ?? Array.Empty<string>())
                .Concat(server ? new[] { "UNITY_SERVER" } : Array.Empty<string>())
                .Concat(development ? new[] { "DEVELOPMENT_BUILD" } : Array.Empty<string>())
                .Concat(assertions ? new[] { "UNITY_ASSERTIONS" } : Array.Empty<string>())
                .Distinct(StringComparer.Ordinal).OrderBy(define => define, StringComparer.Ordinal).ToArray();
        }

        private void ApplyAssemblyDefinitions(BuildPlayerOptions options)
        {
            var selected = assemblies.ToDictionary(assembly => assembly.name, StringComparer.Ordinal);
            var autoReferences = new HashSet<string>(StringComparer.Ordinal);
            var references = assemblies.SelectMany(assembly => assembly.references).Distinct(StringComparer.Ordinal)
                .OrderBy(path => path, StringComparer.Ordinal).ToArray();
            var platforms = CompilationPipeline.GetAssemblyDefinitionPlatforms().Where(platform => platform.BuildTarget == options.target)
                .Select(platform => platform.Name).ToArray();
            var groups = AssetDatabase.GetAllAssetPaths().Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                .GroupBy(CompilationPipeline.GetAssemblyDefinitionFilePathFromScriptPath, StringComparer.Ordinal)
                .Where(group => !string.IsNullOrEmpty(group.Key));
            foreach (var group in groups)
            {
                var definition = JsonConvert.DeserializeObject<AssemblyDefinition>(File.ReadAllText(PlayServPackagePathResolver.ToAbsoluteAssetPath(group.Key)));
                if (definition == null || string.IsNullOrWhiteSpace(definition.name))
                    throw new BuildFailedException("PlayServ: cannot read assembly definition " + group.Key);
                var defines = BuildDefines(CompilationPipeline.GetDefinesFromAssemblyName(definition.name) ?? Array.Empty<string>(), options);
                if (!definition.Includes(platforms, defines))
                {
                    selected.Remove(definition.name);
                    continue;
                }
                if (definition.autoReferenced) autoReferences.Add(definition.name);
                var dependencies = (definition.references ?? Array.Empty<string>()).Select(ResolveAssemblyName).ToArray();
                if (selected.TryGetValue(definition.name, out var existing))
                {
                    existing.assemblyReferences = dependencies;
                    continue;
                }
                selected.Add(definition.name, new PlayServContractBuildAssembly
                {
                    name = definition.name,
                    sources = group.Select(PlayServPackagePathResolver.ToAbsoluteAssetPath).Distinct(StringComparer.Ordinal)
                        .OrderBy(path => path, StringComparer.Ordinal).ToArray(),
                    defines = defines, references = references.Concat(ResolvePlugins(definition, options.target))
                        .Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal).ToArray(),
                    assemblyReferences = dependencies
                });
            }
            foreach (var assembly in selected.Values.Where(assembly =>
                         string.IsNullOrEmpty(CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(assembly.name))))
                assembly.assemblyReferences = assembly.assemblyReferences.Concat(autoReferences).Distinct(StringComparer.Ordinal)
                    .OrderBy(name => name, StringComparer.Ordinal).ToArray();
            assemblies = selected.Values.OrderBy(assembly => assembly.name, StringComparer.Ordinal).ToArray();
        }

        private static string ResolveAssemblyName(string reference)
        {
            if (!reference.StartsWith("GUID:", StringComparison.OrdinalIgnoreCase)) return reference;
            var asset = CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyReference(reference);
            if (string.IsNullOrEmpty(asset)) throw new BuildFailedException("PlayServ: unresolved assembly reference " + reference);
            return JsonConvert.DeserializeObject<AssemblyDefinition>(File.ReadAllText(PlayServPackagePathResolver.ToAbsoluteAssetPath(asset))).name;
        }

        private static IEnumerable<string> ResolvePlugins(AssemblyDefinition definition, BuildTarget target)
        {
            foreach (var name in definition.precompiledReferences ?? Array.Empty<string>())
            {
                var matches = PluginImporter.GetImporters(target).Where(plugin =>
                    string.Equals(Path.GetFileName(plugin.assetPath), name, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matches.Length != 1)
                    throw new BuildFailedException($"PlayServ: cannot resolve precompiled reference '{name}' for '{definition.name}' ({target}).");
                yield return PlayServPackagePathResolver.ToAbsoluteAssetPath(matches[0].assetPath);
            }
        }

        [Serializable]
        private sealed class AssemblyDefinition
        {
            public string name;
            public bool autoReferenced = true;
            public string[] includePlatforms = Array.Empty<string>();
            public string[] excludePlatforms = Array.Empty<string>();
            public string[] defineConstraints = Array.Empty<string>();
            public string[] optionalUnityReferences = Array.Empty<string>();
            public string[] references = Array.Empty<string>();
            public string[] precompiledReferences = Array.Empty<string>();

            public bool Includes(string[] platforms, string[] defines)
            {
                if ((optionalUnityReferences ?? Array.Empty<string>()).Contains("TestAssemblies")) return false;
                if (includePlatforms != null && includePlatforms.Length > 0 && !includePlatforms.Intersect(platforms).Any()) return false;
                if (excludePlatforms != null && excludePlatforms.Intersect(platforms).Any()) return false;
                return (defineConstraints ?? Array.Empty<string>()).All(constraint =>
                    constraint.Split(new[] { "||" }, StringSplitOptions.None).Any(term =>
                    {
                        var symbol = term.Trim();
                        return symbol.StartsWith("!", StringComparison.Ordinal) ? !defines.Contains(symbol.Substring(1).Trim()) : defines.Contains(symbol);
                    }));
            }
        }

        private static string ResolveReference(string path)
        {
            if (File.Exists(path)) return Path.GetFullPath(path);
            var compiled = Path.Combine("Library", "ScriptAssemblies", Path.GetFileName(path));
            if (File.Exists(compiled)) return Path.GetFullPath(compiled);
            throw new BuildFailedException("PlayServ: contract validation cannot read assembly reference " + path + ". Let Unity finish compiling before building.");
        }

        internal bool ContainsContracts()
        {
            var compilations = CreateCompilations();
            foreach (var compilation in compilations)
            {
                // Only attribute-bearing type declarations need semantic resolution. This also
                // keeps projects that do not use contracts independent of the optional tool.
                var candidates = compilation.SyntaxTrees.SelectMany(tree => tree.GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
                    .SelectMany(type => type.AttributeLists.SelectMany(list => list.Attributes)).ToArray();
                if (candidates.Length == 0) continue;
                foreach (var attribute in candidates)
                    if (compilation.GetSemanticModel(attribute.SyntaxTree).GetTypeInfo(attribute).Type?.ToDisplayString()
                        == "Playserv.Schema.PlayServSchemaAttribute") return true;
            }
            return false;
        }

        internal IEnumerable<CSharpCompilation> CreateCompilations()
        {
            var inputs = assemblies.ToDictionary(assembly => assembly.name, StringComparer.Ordinal);
            var completed = new Dictionary<string, CSharpCompilation>(StringComparer.Ordinal);
            var visiting = new HashSet<string>(StringComparer.Ordinal);
            var metadata = new Dictionary<string, PortableExecutableReference>(StringComparer.Ordinal);
            CSharpCompilation Compile(string name)
            {
                if (completed.TryGetValue(name, out var result)) return result;
                if (!visiting.Add(name)) throw new BuildFailedException("PlayServ: cyclic contract assembly reference " + name);
                var input = inputs[name];
                var trees = input.sources.Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path),
                    new CSharpParseOptions(LanguageVersion.Latest, preprocessorSymbols: input.defines), path));
                var references = new List<MetadataReference>();
                foreach (var path in input.references.Where(path => !inputs.ContainsKey(Path.GetFileNameWithoutExtension(path))))
                {
                    if (!metadata.TryGetValue(path, out var reference)) metadata[path] = reference = MetadataReference.CreateFromFile(path);
                    references.Add(reference);
                }
                references.AddRange(input.assemblyReferences.Where(inputs.ContainsKey).Select(dependency => Compile(dependency).ToMetadataReference()));
                result = CSharpCompilation.Create(name, trees, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
                completed.Add(name, result);
                visiting.Remove(name);
                return result;
            }
            return assemblies.Select(input => Compile(input.name)).ToArray();
        }
    }

    [Serializable]
    internal sealed class PlayServContractBuildAssembly
    {
        public string name;
        public string[] sources = Array.Empty<string>();
        public string[] defines = Array.Empty<string>();
        public string[] references = Array.Empty<string>();
        public string[] assemblyReferences = Array.Empty<string>();
    }
}
