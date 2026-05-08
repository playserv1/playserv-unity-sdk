using System;

namespace Playserv.Modules
{
    public sealed class PlayServModuleManifestEntry
    {
        private static readonly string[] EmptyStrings = Array.Empty<string>();

        public PlayServModuleManifestEntry(
            string id,
            string label,
            string description,
            string disableDefine,
            bool defaultEnabled,
            bool isServerModule,
            bool visibleInSettings,
            bool visibleInExport,
            string[] assetPaths,
            string[] dependencyIds,
            string[] hiddenDependencyAssetPaths,
            string[] hiddenDependencyModuleIds,
            string rootAssemblyReference = null)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Module id is required.", nameof(id));

            Id = id;
            Label = string.IsNullOrWhiteSpace(label) ? id : label;
            Description = description ?? string.Empty;
            DisableDefine = disableDefine ?? string.Empty;
            DefaultEnabled = defaultEnabled;
            IsServerModule = isServerModule;
            VisibleInSettings = visibleInSettings;
            VisibleInExport = visibleInExport;
            AssetPaths = CloneOrEmpty(assetPaths);
            DependencyIds = CloneOrEmpty(dependencyIds);
            HiddenDependencyAssetPaths = CloneOrEmpty(hiddenDependencyAssetPaths);
            HiddenDependencyModuleIds = CloneOrEmpty(hiddenDependencyModuleIds);
            RootAssemblyReference = rootAssemblyReference ?? string.Empty;
        }

        public string Id { get; }

        public string Label { get; }

        public string Description { get; }

        public string DisableDefine { get; }

        public bool DefaultEnabled { get; }

        public bool IsServerModule { get; }

        public bool VisibleInSettings { get; }

        public bool VisibleInExport { get; }

        public string[] AssetPaths { get; }

        public string[] DependencyIds { get; }

        public string[] HiddenDependencyAssetPaths { get; }

        public string[] HiddenDependencyModuleIds { get; }

        public string RootAssemblyReference { get; }

        private static string[] CloneOrEmpty(string[] values)
        {
            return values == null || values.Length == 0
                ? EmptyStrings
                : (string[])values.Clone();
        }
    }
}
