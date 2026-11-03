using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Playserv.Deploy.Editor.Analysis.Validators
{
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
}
