using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Playserv.Deploy.Editor.Analysis.Validators
{
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
}
