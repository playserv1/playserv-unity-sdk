using System;

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
        {
            PackageId = packageId ?? throw new ArgumentNullException(nameof(packageId));
            ModuleId = moduleId ?? throw new ArgumentNullException(nameof(moduleId));
            DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
            Description = description ?? string.Empty;
            GitPath = gitPath ?? throw new ArgumentNullException(nameof(gitPath));
        }

        public string PackageId { get; }
        public string ModuleId { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public string GitPath { get; }
    }
}
