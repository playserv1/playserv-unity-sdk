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
                .Select(f => Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(File.ReadAllText(f), path: f))
                .Cast<SyntaxTree>()
                .ToList();

            return AnalyzeInternal(syntaxTrees);
        }

        public AnalysisResult AnalyzeContents(List<string> csFileContents)
        {
            if (csFileContents == null)
                throw new ArgumentNullException(nameof(csFileContents));

            var syntaxTrees = csFileContents
                .Select((content, index) => Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(
                    content ?? string.Empty,
                    path: "File" + index + ".cs"))
                .Cast<SyntaxTree>()
                .ToList();

            return AnalyzeInternal(syntaxTrees);
        }

        private AnalysisResult AnalyzeInternal(List<SyntaxTree> syntaxTrees)
        {
            var compilation = AnalysisCompilationFactory.Create(syntaxTrees);
            var rpcClassList = CollectRpcClasses(syntaxTrees);
            var result = new AnalysisResult { Success = true };

            if (rpcClassList.Count > 0)
                ApplyCircularDependencyValidation(compilation, result);

            ValidateRpcClasses(compilation, rpcClassList, result);

            var dependencyNodes = CollectDependencyNodes(compilation, rpcClassList);
            ValidateDependencyNodes(compilation, dependencyNodes, result);

            var allNodesToCompile = new HashSet<SyntaxNode>(dependencyNodes);
            foreach (var rpcClass in rpcClassList)
                allNodesToCompile.Add(rpcClass);

            result.FilesToCompile = ExtractUniqueFilePaths(allNodesToCompile);
            result.RpcServices = _scanner.ExtractRpcServices(rpcClassList);

            return result;
        }

        private List<ClassDeclarationSyntax> CollectRpcClasses(List<SyntaxTree> syntaxTrees)
        {
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

            return rpcClassList;
        }

        private void ValidateRpcClasses(
            CSharpCompilation compilation,
            IEnumerable<ClassDeclarationSyntax> rpcClassList,
            AnalysisResult result)
        {
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
        }

        private HashSet<SyntaxNode> CollectDependencyNodes(
            CSharpCompilation compilation,
            IEnumerable<ClassDeclarationSyntax> rpcClassList)
        {
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

            return allDependencyNodes;
        }

        private void ValidateDependencyNodes(
            CSharpCompilation compilation,
            IEnumerable<SyntaxNode> dependencyNodes,
            AnalysisResult result)
        {
            foreach (var dependencyNode in dependencyNodes)
            {
                var semanticModel = compilation.GetSemanticModel(dependencyNode.SyntaxTree);
                var validationResult = _dependencyPipeline.Validate(dependencyNode, semanticModel);

                if (!validationResult.IsValid)
                {
                    result.Success = false;
                    result.Errors.AddRange(validationResult.Errors);
                }
            }
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

            var seedRoot = seedTree.GetRoot();
            var seedSemanticModel = compilation.GetSemanticModel(seedTree);
            var circularValidator = new CircularDependencyValidator();
            var circularResult = circularValidator.Validate(seedRoot, seedSemanticModel);

            if (circularResult.IsValid)
                return;

            result.Success = false;
            result.Errors.AddRange(circularResult.Errors);
        }

        private static List<string> ExtractUniqueFilePaths(IEnumerable<SyntaxNode> dependencyNodes)
        {
            return dependencyNodes
                .Select(node => node.SyntaxTree.FilePath)
                .Where(path => !string.IsNullOrEmpty(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
