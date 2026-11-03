using System.Collections.Generic;

namespace Playserv.Deploy.Editor.Analysis.Validators
{
    public sealed class ValidationResult
    {
        public bool IsValid { get; set; }

        public List<string> Errors { get; set; } = new List<string>();
    }
}
