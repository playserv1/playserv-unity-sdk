using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Playserv.Deploy.Editor.Analysis.Validators
{
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
                AddError(
                    result,
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
}
