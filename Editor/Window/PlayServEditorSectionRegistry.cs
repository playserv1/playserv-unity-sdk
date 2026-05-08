using System;
using System.Collections.Generic;

namespace Playserv.Editor
{
    internal static class PlayServEditorSectionRegistry
    {
        private static readonly Dictionary<string, Func<IPlayServEditorSection>> Factories =
            new Dictionary<string, Func<IPlayServEditorSection>>(StringComparer.Ordinal);

        public static void Register(string sectionId, Func<IPlayServEditorSection> factory)
        {
            if (string.IsNullOrWhiteSpace(sectionId))
                throw new ArgumentException("Section id is required.", nameof(sectionId));

            Factories[sectionId] = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        public static bool IsRegistered(string sectionId)
        {
            return !string.IsNullOrWhiteSpace(sectionId) && Factories.ContainsKey(sectionId);
        }

        public static bool TryCreate(string sectionId, out IPlayServEditorSection section)
        {
            section = null;

            if (string.IsNullOrWhiteSpace(sectionId) || !Factories.TryGetValue(sectionId, out var factory))
                return false;

            section = factory();
            return section != null;
        }
    }
}
