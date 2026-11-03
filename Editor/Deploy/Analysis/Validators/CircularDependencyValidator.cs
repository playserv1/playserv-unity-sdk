using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Playserv.Deploy.Editor.Analysis.Validators
{
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
