using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Playserv.Deploy.Editor.Analysis.Validators;

namespace Playserv.Deploy.Editor.Analysis
{
    public sealed class RpcServiceMetadata
    {
        public RpcServiceMetadata(string serviceName, List<string> methods)
        {
            ServiceName = serviceName;
            Methods = methods ?? new List<string>();
        }

        public string ServiceName { get; }

        public List<string> Methods { get; }
    }

    public sealed class AnalysisResult
    {
        public bool Success { get; set; }

        public List<string> Errors { get; set; } = new List<string>();

        public List<string> FilesToCompile { get; set; } = new List<string>();

        public List<RpcServiceMetadata> RpcServices { get; set; } = new List<RpcServiceMetadata>();
    }

    public sealed class FunctionAnalyzerService
    {
        private readonly SyntaxScanner _scanner = new SyntaxScanner();
        private readonly ValidationPipeline _rpcPipeline = new ValidationPipeline();
        private readonly ValidationPipeline _dependencyPipeline = new ValidationPipeline();
        private readonly DependencyScanner _dependencyScanner = new DependencyScanner();

        public FunctionAnalyzerService()
        {
            _rpcPipeline.AddValidator(new NamespaceValidator());
            _rpcPipeline.AddValidator(new RpcConstructorValidator());
            _rpcPipeline.AddValidator(new RpcClassModifierValidator());
            _rpcPipeline.AddValidator(new RpcMethodValidator());

            _dependencyPipeline.AddValidator(new NamespaceValidator());
            _dependencyPipeline.AddValidator(new DependencyTypeValidator());
            _dependencyPipeline.AddValidator(new DependencyReferenceValidator());
        }

        public AnalysisResult AnalyzeFiles(List<string> csFilePaths)
        {
            if (csFilePaths == null)
                throw new ArgumentNullException(nameof(csFilePaths));

            var syntaxTrees = csFilePaths
                .Where(f => !string.IsNullOrWhiteSpace(f) && File.Exists(f))
                .Select(f => CSharpSyntaxTree.ParseText(File.ReadAllText(f), path: f))
                .Cast<SyntaxTree>()
                .ToList();

            return AnalyzeInternal(syntaxTrees);
        }

        public AnalysisResult AnalyzeContents(List<string> csFileContents)
        {
            if (csFileContents == null)
                throw new ArgumentNullException(nameof(csFileContents));

            var syntaxTrees = csFileContents
                .Select((content, index) => CSharpSyntaxTree.ParseText(content ?? string.Empty, path: "File" + index + ".cs"))
                .Cast<SyntaxTree>()
                .ToList();

            return AnalyzeInternal(syntaxTrees);
        }

        private AnalysisResult AnalyzeInternal(List<SyntaxTree> syntaxTrees)
        {
            var compilation = CSharpCompilation.Create(
                "UnityProject",
                syntaxTrees,
                BuildMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var rpcClassList = new List<ClassDeclarationSyntax>();

            foreach (var tree in syntaxTrees)
            {
                var root = tree.GetRoot();
                var rpcClasses = _scanner.FindClassesWithAttribute(root, "Rpc");

                foreach (var rpcClass in rpcClasses)
                {
                    if (!rpcClassList.Contains(rpcClass))
                        rpcClassList.Add(rpcClass);
                }
            }

            var result = new AnalysisResult { Success = true };

            if (rpcClassList.Count > 0)
                ApplyCircularDependencyValidation(compilation, result);

            foreach (var rpcClass in rpcClassList)
            {
                var semanticModel = compilation.GetSemanticModel(rpcClass.SyntaxTree);
                var validationResult = _rpcPipeline.Validate(rpcClass, semanticModel);

                if (!validationResult.IsValid)
                {
                    result.Success = false;
                    result.Errors.AddRange(validationResult.Errors);
                }
            }

            var allDependencyNodes = new HashSet<SyntaxNode>();
            var rpcClassSet = new HashSet<SyntaxNode>(rpcClassList.Cast<SyntaxNode>());

            foreach (var rpcClass in rpcClassList)
            {
                var dependencyGraph = _dependencyScanner.Analyze(rpcClass, compilation);
                var dependencies = dependencyGraph.GetAllDependentNodes().ToList();

                foreach (var node in dependencies)
                {
                    if (!rpcClassSet.Contains(node))
                        allDependencyNodes.Add(node);
                }
            }

            foreach (var dependencyNode in allDependencyNodes)
            {
                var semanticModel = compilation.GetSemanticModel(dependencyNode.SyntaxTree);
                var validationResult = _dependencyPipeline.Validate(dependencyNode, semanticModel);

                if (!validationResult.IsValid)
                {
                    result.Success = false;
                    result.Errors.AddRange(validationResult.Errors);
                }
            }

            var allNodesToCompile = new HashSet<SyntaxNode>(allDependencyNodes);
            foreach (var rpcClass in rpcClassList)
                allNodesToCompile.Add(rpcClass);

            result.FilesToCompile = ExtractUniqueFilePaths(allNodesToCompile);
            result.RpcServices = _scanner.ExtractRpcServices(rpcClassList);

            return result;
        }

        private static void ApplyCircularDependencyValidation(
            CSharpCompilation compilation,
            AnalysisResult result)
        {
            if (compilation == null)
                throw new ArgumentNullException(nameof(compilation));

            if (result == null)
                throw new ArgumentNullException(nameof(result));

            var seedTree = compilation.SyntaxTrees.FirstOrDefault();
            if (seedTree == null)
                return;

            // CircularDependencyValidator walks the entire compilation, so the seed root/model
            // here only provide access to that compilation context.
            var seedRoot = seedTree.GetRoot();
            var seedSemanticModel = compilation.GetSemanticModel(seedTree);
            var circularValidator = new CircularDependencyValidator();
            var circularResult = circularValidator.Validate(seedRoot, seedSemanticModel);

            if (circularResult.IsValid)
                return;

            result.Success = false;
            result.Errors.AddRange(circularResult.Errors);
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

        private static List<string> ExtractUniqueFilePaths(HashSet<SyntaxNode> dependencyNodes)
        {
            return dependencyNodes
                .Select(node => node.SyntaxTree.FilePath)
                .Where(path => !string.IsNullOrEmpty(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    internal sealed class SyntaxScanner
    {
        public IEnumerable<ClassDeclarationSyntax> FindClassesWithAttribute(SyntaxNode root, string attributeName)
        {
            var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>();

            foreach (var classDecl in classes)
            {
                if (HasAttribute(classDecl.AttributeLists, attributeName))
                    yield return classDecl;
            }
        }

        public List<RpcServiceMetadata> ExtractRpcServices(IEnumerable<ClassDeclarationSyntax> rpcClasses)
        {
            return rpcClasses
                .Select(rpcClass => new RpcServiceMetadata(rpcClass.Identifier.Text, ExtractPublicMethods(rpcClass)))
                .ToList();
        }

        private static List<string> ExtractPublicMethods(ClassDeclarationSyntax classDeclaration)
        {
            return classDeclaration.Members
                .OfType<MethodDeclarationSyntax>()
                .Where(m => m.Modifiers.Any(mod => mod.IsKind(SyntaxKind.PublicKeyword)))
                .Select(m => m.Identifier.Text)
                .ToList();
        }

        private static bool HasAttribute(SyntaxList<AttributeListSyntax> attributeLists, string attributeName)
        {
            return attributeLists
                .SelectMany(al => al.Attributes)
                .Any(attr => MatchesAttributeName(attr.Name.ToString(), attributeName));
        }

        private static bool MatchesAttributeName(string name, string targetName)
        {
            return name == targetName || name == targetName + "Attribute";
        }
    }

    internal sealed class DependencyGraph
    {
        private readonly HashSet<SyntaxNode> _typeNodes = new HashSet<SyntaxNode>();

        public void AddTypeNode(SyntaxNode typeNode)
        {
            _typeNodes.Add(typeNode);
        }

        public IEnumerable<SyntaxNode> GetAllDependentNodes()
        {
            return _typeNodes;
        }
    }

    internal sealed class DependencyScanner
    {
        public DependencyGraph Analyze(SyntaxNode rootNode, CSharpCompilation compilation)
        {
            var graph = new DependencyGraph();
            var visited = new HashSet<string>();
            var typeIndex = BuildTypeIndex(compilation);

            TraverseNode(rootNode, compilation, typeIndex, visited, graph);

            return graph;
        }

        private void TraverseNode(
            SyntaxNode node,
            CSharpCompilation compilation,
            Dictionary<string, SyntaxNode> typeIndex,
            HashSet<string> visited,
            DependencyGraph graph)
        {
            var typeName = GetTypeName(node);
            if (string.IsNullOrEmpty(typeName) || visited.Contains(typeName))
                return;

            visited.Add(typeName);
            graph.AddTypeNode(node);

            var semanticModel = compilation.GetSemanticModel(node.SyntaxTree);
            var referencedTypes = FindReferencedTypes(node, semanticModel);

            foreach (var referencedTypeName in referencedTypes)
            {
                SyntaxNode referencedNode;
                if (typeIndex.TryGetValue(referencedTypeName, out referencedNode))
                    TraverseNode(referencedNode, compilation, typeIndex, visited, graph);
            }
        }

        private static HashSet<string> FindReferencedTypes(SyntaxNode node, SemanticModel semanticModel)
        {
            var types = new HashSet<string>();

            foreach (var descendant in node.DescendantNodes())
            {
                ITypeSymbol typeSymbol = null;

                var creation = descendant as ObjectCreationExpressionSyntax;
                if (creation != null)
                {
                    typeSymbol = semanticModel.GetTypeInfo(creation).Type;
                }
                else
                {
                    var identifier = descendant as IdentifierNameSyntax;
                    if (identifier != null)
                    {
                        var symbolInfo = semanticModel.GetSymbolInfo(identifier);
                        var namedType = symbolInfo.Symbol as INamedTypeSymbol;
                        if (namedType != null)
                            typeSymbol = namedType;
                    }
                    else
                    {
                        var typeSyntax = descendant as TypeSyntax;
                        if (typeSyntax != null)
                            typeSymbol = semanticModel.GetTypeInfo(typeSyntax).Type;
                    }
                }

                if (typeSymbol != null && !IsSystemType(typeSymbol))
                    AddTypeAndGenericArguments(typeSymbol, types);
            }

            return types;
        }

        private static void AddTypeAndGenericArguments(ITypeSymbol typeSymbol, HashSet<string> types)
        {
            var typeName = typeSymbol.ToDisplayString();
            types.Add(typeName);

            var namedType = typeSymbol as INamedTypeSymbol;
            if (namedType != null && namedType.IsGenericType)
            {
                foreach (var typeArg in namedType.TypeArguments)
                {
                    if (!IsSystemType(typeArg))
                        AddTypeAndGenericArguments(typeArg, types);
                }
            }
        }

        private static Dictionary<string, SyntaxNode> BuildTypeIndex(CSharpCompilation compilation)
        {
            var index = new Dictionary<string, SyntaxNode>();

            foreach (var tree in compilation.SyntaxTrees)
            {
                var root = tree.GetRoot();
                var typeDeclarations = root.DescendantNodes().Where(n =>
                    n is ClassDeclarationSyntax ||
                    n is StructDeclarationSyntax ||
                    n is EnumDeclarationSyntax ||
                    n is InterfaceDeclarationSyntax ||
                    n is RecordDeclarationSyntax);

                foreach (var typeDecl in typeDeclarations)
                {
                    var typeName = GetTypeName(typeDecl);
                    if (!string.IsNullOrEmpty(typeName) && !index.ContainsKey(typeName))
                        index[typeName] = typeDecl;
                }
            }

            return index;
        }

        private static string GetTypeName(SyntaxNode node)
        {
            string identifier = null;

            var cls = node as ClassDeclarationSyntax;
            if (cls != null)
                identifier = cls.Identifier.Text;

            var str = node as StructDeclarationSyntax;
            if (identifier == null && str != null)
                identifier = str.Identifier.Text;

            var enm = node as EnumDeclarationSyntax;
            if (identifier == null && enm != null)
                identifier = enm.Identifier.Text;

            var iface = node as InterfaceDeclarationSyntax;
            if (identifier == null && iface != null)
                identifier = iface.Identifier.Text;

            var rec = node as RecordDeclarationSyntax;
            if (identifier == null && rec != null)
                identifier = rec.Identifier.Text;

            if (identifier == null)
                return string.Empty;

            var namespaceName = GetNamespace(node);
            return string.IsNullOrEmpty(namespaceName)
                ? identifier
                : namespaceName + "." + identifier;
        }

        private static string GetNamespace(SyntaxNode node)
        {
            var namespaceDecl = node.Ancestors().OfType<NamespaceDeclarationSyntax>().FirstOrDefault();
            if (namespaceDecl != null)
                return namespaceDecl.Name.ToString();

            var fileScopedNamespace = node.Ancestors().OfType<FileScopedNamespaceDeclarationSyntax>().FirstOrDefault();
            return fileScopedNamespace != null ? fileScopedNamespace.Name.ToString() : string.Empty;
        }

        private static bool IsSystemType(ITypeSymbol typeSymbol)
        {
            var ns = typeSymbol.ContainingNamespace != null
                ? typeSymbol.ContainingNamespace.ToDisplayString()
                : string.Empty;

            return ns.StartsWith("System", StringComparison.Ordinal) ||
                   ns.StartsWith("Microsoft", StringComparison.Ordinal) ||
                   typeSymbol.SpecialType != SpecialType.None;
        }
    }
}

namespace Playserv.Deploy.Editor.Analysis.Validators
{
    public sealed class ValidationResult
    {
        public bool IsValid { get; set; }

        public List<string> Errors { get; set; } = new List<string>();
    }

    public abstract class SyntaxValidator : CSharpSyntaxWalker
    {
        public abstract ValidationResult Validate(SyntaxNode root, SemanticModel semanticModel);
    }

    public sealed class ValidationPipeline
    {
        private readonly List<SyntaxValidator> _validators = new List<SyntaxValidator>();

        public void AddValidator(SyntaxValidator validator)
        {
            _validators.Add(validator);
        }

        public ValidationResult Validate(SyntaxNode root, SemanticModel semanticModel)
        {
            var result = new ValidationResult { IsValid = true };

            foreach (var validator in _validators)
            {
                var validationResult = validator.Validate(root, semanticModel);

                if (!validationResult.IsValid)
                {
                    result.IsValid = false;
                    result.Errors.AddRange(validationResult.Errors);
                }
            }

            return result;
        }
    }

    public sealed class NamespaceValidator : SyntaxValidator
    {
        private static readonly HashSet<string> AllowedNamespaces = new HashSet<string>
        {
            "System",
            "System.Collections.Generic",
            "System.Linq",
            "System.Text"
        };

        public override ValidationResult Validate(SyntaxNode root, SemanticModel semanticModel)
        {
            var result = new ValidationResult { IsValid = true };

            foreach (var node in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
            {
                var namespaceName = node.Name != null ? node.Name.ToString() : null;

                if (namespaceName != null && !IsAllowedNamespace(namespaceName))
                    AddError(result, "Invalid namespace: " + namespaceName);
            }

            return result;
        }

        private static bool IsAllowedNamespace(string namespaceName)
        {
            return AllowedNamespaces.Any(allowed =>
                namespaceName == allowed || namespaceName.StartsWith(allowed + ".", StringComparison.Ordinal));
        }

        private static void AddError(ValidationResult result, string error)
        {
            result.IsValid = false;
            result.Errors.Add(error);
        }
    }

    public sealed class RpcConstructorValidator : SyntaxValidator
    {
        public override ValidationResult Validate(SyntaxNode root, SemanticModel semanticModel)
        {
            var result = new ValidationResult { IsValid = true };

            var rootClass = root as ClassDeclarationSyntax;
            if (rootClass != null)
                ValidateClass(rootClass, result, semanticModel);

            foreach (var node in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
                ValidateClass(node, result, semanticModel);

            return result;
        }

        private static void ValidateClass(ClassDeclarationSyntax node, ValidationResult result, SemanticModel semanticModel)
        {
            if (node.ParameterList != null)
            {
                ValidateParameters(node.Identifier.Text, node.ParameterList.Parameters, result, semanticModel);
                return;
            }

            var constructors = node.Members.OfType<ConstructorDeclarationSyntax>().ToList();

            if (constructors.Count > 1)
            {
                AddError(result,
                    "RPC class '" + node.Identifier.Text +
                    "' cannot have multiple constructors. DI requires exactly one constructor");
            }
        }

        private static void ValidateParameters(
            string className,
            SeparatedSyntaxList<ParameterSyntax> parameters,
            ValidationResult result,
            SemanticModel semanticModel)
        {
            if (parameters.Count == 0)
            {
                AddError(result, "RPC class '" + className + "' constructor must have IContext parameter");
                return;
            }

            var hasIContext = false;
            foreach (var parameter in parameters)
            {
                var paramType = parameter.Type != null ? parameter.Type.ToString() : null;

                if (paramType == "IContext")
                {
                    hasIContext = true;
                }
                else if (!IsValidRpcDependency(parameter, semanticModel))
                {
                    AddError(
                        result,
                        "RPC class '" + className +
                        "' constructor can only have IContext or [Rpc] classes as dependencies, found '" +
                        paramType + "'");
                }
            }

            if (!hasIContext)
                AddError(result, "RPC class '" + className + "' constructor must have IContext parameter");
        }

        private static bool IsValidRpcDependency(ParameterSyntax parameter, SemanticModel semanticModel)
        {
            if (parameter.Type == null)
                return false;

            var typeInfo = semanticModel.GetTypeInfo(parameter.Type);
            var typeSymbol = typeInfo.Type;

            if (typeSymbol == null || typeSymbol.TypeKind != TypeKind.Class)
                return false;

            return typeSymbol.GetAttributes()
                .Any(attr => attr.AttributeClass != null &&
                             (attr.AttributeClass.Name == "RpcAttribute" || attr.AttributeClass.Name == "Rpc"));
        }

        private static void AddError(ValidationResult result, string error)
        {
            result.IsValid = false;
            result.Errors.Add(error);
        }
    }

    public sealed class RpcClassModifierValidator : SyntaxValidator
    {
        public override ValidationResult Validate(SyntaxNode root, SemanticModel semanticModel)
        {
            var result = new ValidationResult { IsValid = true };

            foreach (var node in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
                ValidateClass(node, result);

            return result;
        }

        private static void ValidateClass(ClassDeclarationSyntax node, ValidationResult result)
        {
            var className = node.Identifier.Text;

            if (!node.Modifiers.Any(m => m.Text == "public"))
                AddError(result, "RPC class '" + className + "' must be public");

            if (node.Modifiers.Any(m => m.Text == "static"))
                AddError(result, "RPC class '" + className + "' cannot be static");

            if (node.Modifiers.Any(m => m.Text == "abstract"))
                AddError(result, "RPC class '" + className + "' cannot be abstract");

            if (node.Parent is ClassDeclarationSyntax)
                AddError(result, "RPC class '" + className + "' cannot be nested inside another class");

            if (node.TypeParameterList != null && node.TypeParameterList.Parameters.Count > 0)
                AddError(result, "RPC class '" + className + "' cannot have generic type parameters");
        }

        private static void AddError(ValidationResult result, string error)
        {
            result.IsValid = false;
            result.Errors.Add(error);
        }
    }

    public sealed class RpcMethodValidator : SyntaxValidator
    {
        public override ValidationResult Validate(SyntaxNode root, SemanticModel semanticModel)
        {
            var result = new ValidationResult { IsValid = true };

            foreach (var node in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
                ValidateClass(node, result);

            return result;
        }

        private static void ValidateClass(ClassDeclarationSyntax node, ValidationResult result)
        {
            var className = node.Identifier.Text;

            var publicMethods = node.Members
                .OfType<MethodDeclarationSyntax>()
                .Where(m => m.Modifiers.Any(mod => mod.Text == "public"))
                .ToList();

            var methodGroups = publicMethods.GroupBy(m => m.Identifier.Text);
            foreach (var group in methodGroups.Where(g => g.Count() > 1))
            {
                AddError(
                    result,
                    "RPC class '" + className + "' has overloaded method '" + group.Key +
                    "'. Method overloading is not allowed in RPC classes");
            }

            foreach (var method in publicMethods)
                ValidateMethod(className, method, result);
        }

        private static void ValidateMethod(string className, MethodDeclarationSyntax method, ValidationResult result)
        {
            var methodName = method.Identifier.Text;

            if (method.Modifiers.Any(m => m.Text == "static"))
                AddError(result, "RPC method '" + className + "." + methodName + "' cannot be static");

            if (method.Modifiers.Any(m => m.Text == "async") && method.ReturnType.ToString() == "void")
            {
                AddError(
                    result,
                    "RPC method '" + className + "." + methodName +
                    "' cannot be async void. Use async Task instead");
            }

            var returnType = method.ReturnType.ToString();
            if (!IsResultType(returnType))
            {
                AddError(
                    result,
                    "RPC method '" + className + "." + methodName +
                    "' must return Result<T> type. Current return type: " + returnType);
            }

            foreach (var parameter in method.ParameterList.Parameters)
                ValidateParameter(className, methodName, parameter, result);
        }

        private static void ValidateParameter(
            string className,
            string methodName,
            ParameterSyntax parameter,
            ValidationResult result)
        {
            var paramName = parameter.Identifier.Text;

            if (parameter.Modifiers.Any(m => m.Text == "ref"))
            {
                AddError(
                    result,
                    "RPC method '" + className + "." + methodName +
                    "' parameter '" + paramName + "' cannot use 'ref' modifier");
            }

            if (parameter.Modifiers.Any(m => m.Text == "out"))
            {
                AddError(
                    result,
                    "RPC method '" + className + "." + methodName +
                    "' parameter '" + paramName + "' cannot use 'out' modifier");
            }

            if (parameter.Modifiers.Any(m => m.Text == "in"))
            {
                AddError(
                    result,
                    "RPC method '" + className + "." + methodName +
                    "' parameter '" + paramName + "' cannot use 'in' modifier");
            }

            if (parameter.Type is PointerTypeSyntax)
            {
                AddError(
                    result,
                    "RPC method '" + className + "." + methodName +
                    "' parameter '" + paramName + "' cannot be a pointer type");
            }
        }

        private static bool IsResultType(string returnType)
        {
            return returnType == "Result" ||
                   (returnType.StartsWith("Result<", StringComparison.Ordinal) && returnType.EndsWith(">", StringComparison.Ordinal)) ||
                   returnType == "Task<Result>" ||
                   (returnType.StartsWith("Task<Result<", StringComparison.Ordinal) && returnType.EndsWith(">>", StringComparison.Ordinal));
        }

        private static void AddError(ValidationResult result, string error)
        {
            result.IsValid = false;
            result.Errors.Add(error);
        }
    }

    public sealed class DependencyTypeValidator : SyntaxValidator
    {
        private static readonly HashSet<string> AllowedTypesWithMethods = new HashSet<string> { "Result" };

        public override ValidationResult Validate(SyntaxNode root, SemanticModel semanticModel)
        {
            var result = new ValidationResult { IsValid = true };

            foreach (var node in root.DescendantNodes())
            {
                var classDecl = node as ClassDeclarationSyntax;
                if (classDecl != null)
                {
                    ValidateClass(classDecl, result);
                    continue;
                }

                var structDecl = node as StructDeclarationSyntax;
                if (structDecl != null)
                {
                    ValidateStruct(structDecl, result);
                    continue;
                }

                var enumDecl = node as EnumDeclarationSyntax;
                if (enumDecl != null)
                {
                    ValidateEnum(enumDecl, result);
                    continue;
                }

                var recordDecl = node as RecordDeclarationSyntax;
                if (recordDecl != null)
                {
                    ValidateRecord(recordDecl, result);
                    continue;
                }

                var interfaceDecl = node as InterfaceDeclarationSyntax;
                if (interfaceDecl != null)
                    ValidateInterface(interfaceDecl, result);
            }

            return result;
        }

        private static void ValidateClass(ClassDeclarationSyntax node, ValidationResult result)
        {
            var className = node.Identifier.Text;

            if (AllowedTypesWithMethods.Contains(className))
                return;

            if (!node.Modifiers.Any(m => m.Text == "public"))
                AddError(result, "Dependency class '" + className + "' must be public");

            if (node.Modifiers.Any(m => m.Text == "static"))
                AddError(result, "Dependency class '" + className + "' cannot be static");

            if (node.Modifiers.Any(m => m.Text == "abstract"))
                AddError(result, "Dependency class '" + className + "' cannot be abstract");

            if (node.TypeParameterList != null && node.TypeParameterList.Parameters.Count > 0)
            {
                AddError(result, "Dependency class '" + className + "' cannot have generic type parameters");
            }

            if (node.Parent is ClassDeclarationSyntax)
                AddError(result, "Dependency class '" + className + "' cannot be nested inside another class");

            var methods = node.Members.OfType<MethodDeclarationSyntax>().ToList();
            if (methods.Count > 0)
            {
                AddError(
                    result,
                    "Dependency class '" + className + "' cannot have methods. Dependencies must be simple data classes (POCOs)");
            }
        }

        private static void ValidateStruct(StructDeclarationSyntax node, ValidationResult result)
        {
            var structName = node.Identifier.Text;

            if (!node.Modifiers.Any(m => m.Text == "public"))
                AddError(result, "Dependency struct '" + structName + "' must be public");

            if (node.TypeParameterList != null && node.TypeParameterList.Parameters.Count > 0)
            {
                AddError(result, "Dependency struct '" + structName + "' cannot have generic type parameters");
            }

            var methods = node.Members.OfType<MethodDeclarationSyntax>().ToList();
            if (methods.Count > 0)
            {
                AddError(
                    result,
                    "Dependency struct '" + structName +
                    "' cannot have methods. Dependencies must be simple data structures");
            }
        }

        private static void ValidateEnum(EnumDeclarationSyntax node, ValidationResult result)
        {
            var enumName = node.Identifier.Text;

            if (!node.Modifiers.Any(m => m.Text == "public"))
                AddError(result, "Dependency enum '" + enumName + "' must be public");
        }

        private static void ValidateRecord(RecordDeclarationSyntax node, ValidationResult result)
        {
            var recordName = node.Identifier.Text;

            if (!node.Modifiers.Any(m => m.Text == "public"))
                AddError(result, "Dependency record '" + recordName + "' must be public");

            if (node.TypeParameterList != null && node.TypeParameterList.Parameters.Count > 0)
            {
                AddError(result, "Dependency record '" + recordName + "' cannot have generic type parameters");
            }

            var methods = node.Members.OfType<MethodDeclarationSyntax>().ToList();
            if (methods.Count > 0)
            {
                AddError(
                    result,
                    "Dependency record '" + recordName + "' cannot have methods. Dependencies must be simple data records");
            }
        }

        private static void ValidateInterface(InterfaceDeclarationSyntax node, ValidationResult result)
        {
            var interfaceName = node.Identifier.Text;

            if (!node.Modifiers.Any(m => m.Text == "public"))
                AddError(result, "Dependency interface '" + interfaceName + "' must be public");

            if (node.TypeParameterList != null && node.TypeParameterList.Parameters.Count > 0)
            {
                AddError(result, "Dependency interface '" + interfaceName + "' cannot have generic type parameters");
            }
        }

        private static void AddError(ValidationResult result, string error)
        {
            result.IsValid = false;
            result.Errors.Add(error);
        }
    }

    public sealed class DependencyReferenceValidator : SyntaxValidator
    {
        private static readonly HashSet<string> AllowedSystemTypes = new HashSet<string>
        {
            "System.String",
            "System.Int32",
            "System.Int64",
            "System.Boolean",
            "System.Double",
            "System.Float",
            "System.Decimal",
            "System.DateTime",
            "System.DateTimeOffset",
            "System.Guid",
            "System.TimeSpan",
            "System.Byte",
            "System.SByte",
            "System.Int16",
            "System.UInt16",
            "System.UInt32",
            "System.UInt64",
            "System.Char",
            "System.Collections.Generic.List",
            "System.Collections.Generic.Dictionary",
            "System.Collections.Generic.HashSet",
            "System.Collections.Generic.IEnumerable",
            "System.Collections.Generic.IList",
            "System.Collections.Generic.IDictionary",
            "System.Nullable"
        };

        public override ValidationResult Validate(SyntaxNode root, SemanticModel semanticModel)
        {
            var result = new ValidationResult { IsValid = true };

            foreach (var node in root.DescendantNodes())
            {
                var classDecl = node as ClassDeclarationSyntax;
                if (classDecl != null)
                {
                    ValidateTypeReferences(classDecl, classDecl.Identifier.Text, "class", result);
                    continue;
                }

                var structDecl = node as StructDeclarationSyntax;
                if (structDecl != null)
                {
                    ValidateTypeReferences(structDecl, structDecl.Identifier.Text, "struct", result);
                    continue;
                }

                var recordDecl = node as RecordDeclarationSyntax;
                if (recordDecl != null)
                    ValidateTypeReferences(recordDecl, recordDecl.Identifier.Text, "record", result);
            }

            return result;
        }

        private static void ValidateTypeReferences(
            TypeDeclarationSyntax node,
            string typeName,
            string typeKind,
            ValidationResult result)
        {
            if (node.BaseList != null)
            {
                foreach (var baseType in node.BaseList.Types)
                {
                    var baseTypeName = baseType.Type.ToString();

                    if (baseTypeName.IndexOf(".", StringComparison.Ordinal) < 0)
                        continue;

                    if (!IsAllowedType(baseTypeName))
                    {
                        AddError(
                            result,
                            "Dependency " + typeKind + " '" + typeName +
                            "' cannot inherit from or implement external type '" + baseTypeName +
                            "'. Only user-defined types are allowed");
                    }
                }
            }

            foreach (var property in node.Members.OfType<PropertyDeclarationSyntax>())
                ValidatePropertyType(property, typeName, typeKind, result);

            foreach (var field in node.Members.OfType<FieldDeclarationSyntax>())
                ValidateFieldType(field, typeName, typeKind, result);
        }

        private static void ValidatePropertyType(
            PropertyDeclarationSyntax property,
            string typeName,
            string typeKind,
            ValidationResult result)
        {
            var propertyType = property.Type.ToString();
            var propertyName = property.Identifier.Text;

            ValidateType(propertyType, typeName, typeKind, "property '" + propertyName + "'", result);
        }

        private static void ValidateFieldType(
            FieldDeclarationSyntax field,
            string typeName,
            string typeKind,
            ValidationResult result)
        {
            var fieldType = field.Declaration.Type.ToString();

            foreach (var variable in field.Declaration.Variables)
            {
                var fieldName = variable.Identifier.Text;
                ValidateType(fieldType, typeName, typeKind, "field '" + fieldName + "'", result);
            }
        }

        private static void ValidateType(
            string type,
            string typeName,
            string typeKind,
            string memberDescription,
            ValidationResult result)
        {
            var baseType = ExtractBaseType(type);

            if (baseType.IndexOf(".", StringComparison.Ordinal) < 0 && !IsGenericCollection(type))
                return;

            if (!IsAllowedType(baseType))
            {
                AddError(
                    result,
                    "Dependency " + typeKind + " '" + typeName + "' " + memberDescription +
                    " uses external type '" + type + "'. Only user-defined types and basic BCL types are allowed");
            }
        }

        private static string ExtractBaseType(string type)
        {
            var genericIndex = type.IndexOf('<');
            if (genericIndex > 0)
                return type.Substring(0, genericIndex);

            if (type.EndsWith("?", StringComparison.Ordinal))
                return type.Substring(0, type.Length - 1);

            return type;
        }

        private static bool IsGenericCollection(string type)
        {
            return type.StartsWith("List<", StringComparison.Ordinal) ||
                   type.StartsWith("Dictionary<", StringComparison.Ordinal) ||
                   type.StartsWith("HashSet<", StringComparison.Ordinal) ||
                   type.StartsWith("IEnumerable<", StringComparison.Ordinal) ||
                   type.StartsWith("IList<", StringComparison.Ordinal) ||
                   type.StartsWith("IDictionary<", StringComparison.Ordinal);
        }

        private static bool IsAllowedType(string type)
        {
            if (AllowedSystemTypes.Contains(type))
                return true;

            if (type.IndexOf(".", StringComparison.Ordinal) < 0)
                return true;

            return AllowedSystemTypes.Any(allowed => type.StartsWith(allowed, StringComparison.Ordinal));
        }

        private static void AddError(ValidationResult result, string error)
        {
            result.IsValid = false;
            result.Errors.Add(error);
        }
    }

    public sealed class CircularDependencyValidator : SyntaxValidator
    {
        public override ValidationResult Validate(SyntaxNode root, SemanticModel semanticModel)
        {
            var result = new ValidationResult { IsValid = true };

            var rpcClasses = new Dictionary<string, List<string>>();

            foreach (var tree in semanticModel.Compilation.SyntaxTrees)
            {
                var treeRoot = tree.GetRoot();
                foreach (var classDecl in treeRoot.DescendantNodes().OfType<ClassDeclarationSyntax>())
                {
                    if (HasRpcAttribute(classDecl, semanticModel.Compilation.GetSemanticModel(tree)))
                    {
                        var className = classDecl.Identifier.Text;
                        var dependencies = GetRpcDependencies(classDecl, semanticModel.Compilation.GetSemanticModel(tree));
                        rpcClasses[className] = dependencies;
                    }
                }
            }

            foreach (var pair in rpcClasses)
            {
                var className = pair.Key;
                var visited = new HashSet<string>();
                var path = new List<string>();

                if (HasCircularDependency(className, rpcClasses, visited, path))
                {
                    var cycle = string.Join(" -> ", path.ToArray());
                    AddError(result, "Circular dependency detected: " + cycle);
                }
            }

            return result;
        }

        private static bool HasRpcAttribute(ClassDeclarationSyntax classDecl, SemanticModel semanticModel)
        {
            var classSymbol = semanticModel.GetDeclaredSymbol(classDecl);
            if (classSymbol == null)
                return false;

            return classSymbol.GetAttributes()
                .Any(attr => attr.AttributeClass != null &&
                             (attr.AttributeClass.Name == "RpcAttribute" || attr.AttributeClass.Name == "Rpc"));
        }

        private static List<string> GetRpcDependencies(ClassDeclarationSyntax classDecl, SemanticModel semanticModel)
        {
            var dependencies = new List<string>();

            var parameterLists = classDecl.ParameterList != null
                ? new[] { classDecl.ParameterList }
                : classDecl.Members.OfType<ConstructorDeclarationSyntax>().Select(c => c.ParameterList);

            foreach (var paramList in parameterLists)
            {
                foreach (var parameter in paramList.Parameters)
                {
                    if (parameter.Type == null)
                        continue;

                    var typeInfo = semanticModel.GetTypeInfo(parameter.Type);
                    var typeSymbol = typeInfo.Type;

                    if (typeSymbol == null || typeSymbol.TypeKind != TypeKind.Class)
                        continue;

                    var hasRpcAttribute = typeSymbol.GetAttributes()
                        .Any(attr => attr.AttributeClass != null &&
                                     (attr.AttributeClass.Name == "RpcAttribute" || attr.AttributeClass.Name == "Rpc"));

                    if (hasRpcAttribute)
                        dependencies.Add(typeSymbol.Name);
                }
            }

            return dependencies;
        }

        private static bool HasCircularDependency(
            string className,
            Dictionary<string, List<string>> rpcClasses,
            HashSet<string> visited,
            List<string> path)
        {
            if (path.Contains(className))
            {
                path.Add(className);
                return true;
            }

            if (visited.Contains(className))
                return false;

            visited.Add(className);
            path.Add(className);

            List<string> dependencies;
            if (rpcClasses.TryGetValue(className, out dependencies))
            {
                foreach (var dependency in dependencies)
                {
                    if (HasCircularDependency(dependency, rpcClasses, visited, path))
                        return true;
                }
            }

            path.RemoveAt(path.Count - 1);
            return false;
        }

        private static void AddError(ValidationResult result, string error)
        {
            result.IsValid = false;
            result.Errors.Add(error);
        }
    }
}
