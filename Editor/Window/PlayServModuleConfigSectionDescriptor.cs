using System;
using Playserv.Modules;

namespace Playserv.Editor
{
    internal sealed class PlayServModuleConfigSectionDescriptor
    {
        public PlayServModuleConfigSectionDescriptor(
            string moduleId,
            string sectionId,
            int order,
            bool requiresEnabled)
        {
            if (string.IsNullOrWhiteSpace(moduleId))
                throw new ArgumentException("Module id is required.", nameof(moduleId));

            if (string.IsNullOrWhiteSpace(sectionId))
                throw new ArgumentException("Section id is required.", nameof(sectionId));

            ModuleId = moduleId;
            SectionId = sectionId;
            Order = order;
            RequiresEnabled = requiresEnabled;
        }

        public string ModuleId { get; }

        public string SectionId { get; }

        public int Order { get; }

        public bool RequiresEnabled { get; }

        public bool ShouldDraw(PlayServEditorModuleSettings settings)
        {
            if (!PlayServModuleManifest.TryGet(ModuleId, out var module))
                return false;

            if (!PlayServEditorModuleAvailability.IsRuntimeModuleAvailable(module))
                return false;

            return !RequiresEnabled || (settings != null && settings.IsRuntimeModuleEnabled(module.Id));
        }
    }
}
