using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Playserv.Deploy.Editor.Analysis.Validators
{
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
}
