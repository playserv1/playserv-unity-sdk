using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Playserv.Deploy.Editor.Analysis
{
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
