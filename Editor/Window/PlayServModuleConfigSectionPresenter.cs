using System;
using System.Collections.Generic;
using UnityEngine;

namespace Playserv.Editor
{
    internal sealed class PlayServModuleConfigSectionPresenter : IDisposable
    {
        private readonly Dictionary<string, PlayServOptionalEditorSection> _sections =
            new Dictionary<string, PlayServOptionalEditorSection>(StringComparer.Ordinal);

        public void Draw(PlayServWindowContext context)
        {
            if (context == null)
                return;

            foreach (var descriptor in PlayServModuleConfigSectionRegistry.RegisteredSections)
            {
                if (!descriptor.ShouldDraw(context.State.ModuleSettings))
                    continue;

                var section = GetOrCreateSection(descriptor.SectionId);
                if (!section.IsAvailable)
                    continue;

                GUILayout.Space(12f);
                section.Draw(context);
            }
        }

        public void Dispose()
        {
            foreach (var section in _sections.Values)
                section.Dispose();

            _sections.Clear();
        }

        private PlayServOptionalEditorSection GetOrCreateSection(string sectionId)
        {
            if (_sections.TryGetValue(sectionId, out var section))
                return section;

            section = new PlayServOptionalEditorSection(sectionId);
            _sections.Add(sectionId, section);
            return section;
        }
    }
}
