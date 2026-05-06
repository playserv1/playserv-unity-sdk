using System;

namespace Playserv.Modules
{
    public sealed class PlayServSdkProfile
    {
        private static readonly string[] EmptyModuleIds = Array.Empty<string>();

        public PlayServSdkProfile(
            string id,
            string label,
            string description,
            string[] enabledModuleIds)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Profile id is required.", nameof(id));

            Id = id;
            Label = string.IsNullOrWhiteSpace(label) ? id : label;
            Description = description ?? string.Empty;
            EnabledModuleIds = enabledModuleIds == null || enabledModuleIds.Length == 0
                ? EmptyModuleIds
                : (string[])enabledModuleIds.Clone();
        }

        public string Id { get; }

        public string Label { get; }

        public string Description { get; }

        public string[] EnabledModuleIds { get; }

        public bool EnablesModule(string moduleId)
        {
            if (string.IsNullOrWhiteSpace(moduleId))
                return false;

            for (var i = 0; i < EnabledModuleIds.Length; i++)
            {
                if (string.Equals(EnabledModuleIds[i], moduleId, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
    }
}
