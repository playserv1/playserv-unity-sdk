using System;
using System.Collections.Generic;
using System.Linq;

namespace Playserv.Editor
{
    internal static class PlayServModuleConfigSectionRegistry
    {
        private static readonly List<PlayServModuleConfigSectionDescriptor> Sections =
            new List<PlayServModuleConfigSectionDescriptor>();

        public static IReadOnlyList<PlayServModuleConfigSectionDescriptor> RegisteredSections =>
            Sections
                .OrderBy(section => section.Order)
                .ThenBy(section => section.SectionId, StringComparer.Ordinal)
                .ToArray();

        public static void Register(
            string moduleId,
            string sectionId,
            int order,
            bool requiresEnabled = true)
        {
            var descriptor = new PlayServModuleConfigSectionDescriptor(
                moduleId,
                sectionId,
                order,
                requiresEnabled);

            if (Sections.Any(section => string.Equals(section.SectionId, descriptor.SectionId, StringComparison.Ordinal)))
                return;

            Sections.Add(descriptor);
        }
    }
}
