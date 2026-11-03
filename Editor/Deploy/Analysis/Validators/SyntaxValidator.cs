using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Playserv.Deploy.Editor.Analysis.Validators
{
    public abstract class SyntaxValidator : CSharpSyntaxWalker
    {
        public abstract ValidationResult Validate(SyntaxNode root, SemanticModel semanticModel);
    }
}
