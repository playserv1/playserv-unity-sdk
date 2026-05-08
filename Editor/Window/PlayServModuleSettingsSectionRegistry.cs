using System;
using System.Collections.Generic;
using System.Linq;

namespace Playserv.Editor
{
    internal static class PlayServModuleSettingsSectionRegistry
    {
        private static readonly List<IPlayServModuleSettingsSection> Sections = new List<IPlayServModuleSettingsSection>();

        public static IReadOnlyList<IPlayServModuleSettingsSection> RegisteredSections =>
            Sections
                .OrderBy(section => section.Order)
                .ThenBy(section => section.GetType().FullName, StringComparer.Ordinal)
                .ToArray();

        public static void Register(IPlayServModuleSettingsSection section)
        {
            if (section == null)
                throw new ArgumentNullException(nameof(section));

            var sectionType = section.GetType();
            if (Sections.Any(existing => existing.GetType() == sectionType))
                return;

            Sections.Add(section);
        }
    }
}
