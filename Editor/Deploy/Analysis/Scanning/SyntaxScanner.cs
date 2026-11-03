using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Playserv.Deploy.Editor.Analysis
{
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
                .Where(m => m.Modifiers.Any(mod => mod.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PublicKeyword)))
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
}
