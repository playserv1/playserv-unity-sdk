using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Playserv.Deploy.Editor.Analysis.Validators
{
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
}
