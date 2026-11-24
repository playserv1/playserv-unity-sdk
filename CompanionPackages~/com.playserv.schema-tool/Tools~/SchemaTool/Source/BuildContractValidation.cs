using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PlayServ.Schema.Tool;

internal sealed class ContractSource
{
    public SyntaxTree Tree { get; }
    public SemanticModel? Model { get; }

    public ContractSource(string path)
    {
        Tree = CSharpSyntaxTree.ParseText(File.ReadAllText(path), new CSharpParseOptions(LanguageVersion.Latest), path);
    }

    public ContractSource(SyntaxTree tree, SemanticModel model) { Tree = tree; Model = model; }
}

internal sealed class ContractBuildInputs
{
    public int Version { get; set; }
    public List<ContractBuildAssembly> Assemblies { get; set; } = new();
}

internal sealed class ContractBuildAssembly
{
    public string Name { get; set; } = string.Empty;
    public List<string> Sources { get; set; } = new();
    public List<string> Defines { get; set; } = new();
    public List<string> References { get; set; } = new();
    public List<string> AssemblyReferences { get; set; } = new();
}

internal static class BuildContractValidation
{
    public static AnalysisSnapshot Analyze(string projectRoot, string inputsPath)
    {
        if (string.IsNullOrWhiteSpace(inputsPath))
            throw new ArgumentException("validate-contracts requires --build-inputs from the Unity build context.");
        var inputs = JsonSerializer.Deserialize<ContractBuildInputs>(File.ReadAllText(inputsPath), ProjectConfiguration.CreateJsonOptions());
        if (inputs == null || inputs.Version != 1 || inputs.Assemblies == null)
            throw new InvalidDataException("Unsupported or missing contract build input version (expected 1).");
        var assemblies = inputs.Assemblies.ToDictionary(assembly => assembly.Name, StringComparer.Ordinal);
        var completed = new Dictionary<string, CSharpCompilation>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var metadata = new Dictionary<string, PortableExecutableReference>(StringComparer.Ordinal);
        CSharpCompilation Compile(string name)
        {
            if (completed.TryGetValue(name, out var existing)) return existing;
            if (!visiting.Add(name)) throw new InvalidDataException("Cyclic contract assembly reference: " + name);
            var assembly = assemblies[name];
            if (string.IsNullOrWhiteSpace(assembly.Name) || assembly.Sources == null || assembly.Defines == null || assembly.References == null || assembly.AssemblyReferences == null)
                throw new InvalidDataException("Invalid contract build assembly input.");
            var trees = assembly.Sources.Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal)
                .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path),
                    new CSharpParseOptions(LanguageVersion.Latest, preprocessorSymbols: assembly.Defines), path)).ToArray();
            var references = new List<MetadataReference>();
            foreach (var path in assembly.References.Distinct(StringComparer.Ordinal).Where(path => !assemblies.ContainsKey(Path.GetFileNameWithoutExtension(path))))
            {
                if (!metadata.TryGetValue(path, out var reference)) metadata[path] = reference = MetadataReference.CreateFromFile(path);
                references.Add(reference);
            }
            references.AddRange(assembly.AssemblyReferences.Where(assemblies.ContainsKey).Select(dependency => Compile(dependency).ToMetadataReference()));
            var compilation = CSharpCompilation.Create(assembly.Name, trees, references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            completed.Add(name, compilation);
            visiting.Remove(name);
            return compilation;
        }
        var sources = inputs.Assemblies.OrderBy(assembly => assembly.Name, StringComparer.Ordinal).Select(assembly => Compile(assembly.Name))
            .SelectMany(compilation => compilation.SyntaxTrees.Select(tree => new ContractSource(tree, compilation.GetSemanticModel(tree)))).ToArray();
        var analysis = CSharpSchemaAnalyzer.Analyze(projectRoot, new SchemaToolConfiguration(), sources);
        return new AnalysisSnapshot
        {
            SourceFiles = analysis.SourceFiles, Contracts = analysis.Contracts,
            Diagnostics = analysis.Diagnostics.Concat(ValidateAttributes(projectRoot, sources))
                .OrderBy(diagnostic => diagnostic.Severity, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.Path, StringComparer.Ordinal).ThenBy(diagnostic => diagnostic.Line)
                .ThenBy(diagnostic => diagnostic.Column).ThenBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.Member, StringComparer.Ordinal).ThenBy(diagnostic => diagnostic.Message, StringComparer.Ordinal).ToArray()
        };
    }

    private static IEnumerable<SchemaDiagnostic> ValidateAttributes(string projectRoot, IEnumerable<ContractSource> sources)
    {
        foreach (var source in sources)
        foreach (var attribute in source.Tree.GetRoot().DescendantNodes().OfType<AttributeSyntax>())
        {
            var model = source.Model!;
            if (model.GetTypeInfo(attribute).Type?.ContainingNamespace.ToDisplayString() != "Playserv.Schema") continue;
            foreach (var argument in attribute.ArgumentList?.Arguments ?? default)
            {
                var constant = model.GetConstantValue(argument.Expression);
                var type = model.GetTypeInfo(argument.Expression).ConvertedType;
                if (constant.HasValue && (type?.TypeKind != TypeKind.Enum || type.GetMembers().OfType<IFieldSymbol>()
                        .Any(field => field.HasConstantValue && Equals(field.ConstantValue, constant.Value)))) continue;
                var owner = attribute.Ancestors().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault();
                var property = attribute.Ancestors().OfType<PropertyDeclarationSyntax>().FirstOrDefault();
                var field = attribute.Ancestors().OfType<FieldDeclarationSyntax>().FirstOrDefault();
                var location = argument.GetLocation().GetLineSpan().StartLinePosition;
                yield return new SchemaDiagnostic
                {
                    Severity = "error", Code = "PSSCHEMA005",
                    Message = $"Cannot resolve supported constant metadata '{argument}' for a PlayServ attribute. Check the value and build assembly references.",
                    Path = Path.GetRelativePath(projectRoot, source.Tree.FilePath).Replace('\\', '/'),
                    Line = location.Line + 1, Column = location.Character + 1,
                    Type = owner == null ? string.Empty : model.GetDeclaredSymbol(owner)?.ToDisplayString() ?? owner.Identifier.ValueText,
                    Member = property?.Identifier.ValueText ?? field?.Declaration.Variables.FirstOrDefault()?.Identifier.ValueText ?? string.Empty
                };
            }
        }
    }

    public static void ValidateReferences(IReadOnlyList<SchemaContract> contracts, ICollection<SchemaDiagnostic> diagnostics)
    {
        var byType = SchemaGenerator.BuildContractTypeIndex(contracts);
        foreach (var contract in contracts)
        foreach (var member in contract.Members)
        {
            try
            {
                SchemaGenerator.ValidateMemberType(member, byType);
                SchemaPushPayloadBuilder.ValidateField(contract, member, byType);
            }
            catch (InvalidOperationException exception)
            {
                diagnostics.Add(new SchemaDiagnostic
                {
                    Severity = "error", Code = "PSSCHEMA004", Message = exception.Message,
                    Path = member.SourcePath, Line = member.SourceLine, Column = member.SourceColumn,
                    Type = contract.FullName, Member = member.SourceName
                });
            }
        }
    }
}
