using System;
using System.Collections.Generic;
using System.Linq;

namespace Playserv.Editor
{
    internal sealed class PlayServCompanionPackageDefinition
    {
        public PlayServCompanionPackageDefinition(
            string packageId,
            string moduleId,
            string displayName,
            string description,
            string gitPath)
            : this(
                packageId,
                new[] { moduleId },
                displayName,
                description,
                gitPath)
        {
        }

        public PlayServCompanionPackageDefinition(
            string packageId,
            IEnumerable<string> moduleIds,
            string displayName,
            string description,
            string gitPath)
        {
            PackageId = packageId ?? throw new ArgumentNullException(nameof(packageId));
            ModuleIds = (moduleIds ?? throw new ArgumentNullException(nameof(moduleIds)))
                .Where(moduleId => !string.IsNullOrWhiteSpace(moduleId))
                .Select(moduleId => moduleId.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (ModuleIds.Count == 0)
            {
                throw new ArgumentException(
                    "At least one module id is required.",
                    nameof(moduleIds));
            }

            DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
            Description = description ?? string.Empty;
            GitPath = gitPath ?? throw new ArgumentNullException(nameof(gitPath));
        }

        public string PackageId { get; }
        public IReadOnlyList<string> ModuleIds { get; }
        public string ModuleId => ModuleIds[0];
        public string DisplayName { get; }
        public string Description { get; }
        public string GitPath { get; }

        public bool ContainsModule(string moduleId)
        {
            return ModuleIds.Any(candidate =>
                string.Equals(candidate, moduleId, StringComparison.Ordinal));
        }
    }
}
