using System;
using System.Collections.Generic;

namespace Playserv.Modules
{
    public sealed class PlayServModuleDescriptor
    {
        private static readonly IReadOnlyList<string> EmptyDependencies = Array.Empty<string>();

        public PlayServModuleDescriptor(string id, bool isCore, params string[] dependencies)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Module id is required.", nameof(id));

            Id = id;
            IsCore = isCore;
            Dependencies = dependencies == null || dependencies.Length == 0
                ? EmptyDependencies
                : Array.AsReadOnly((string[])dependencies.Clone());
        }

        public string Id { get; }

        public bool IsCore { get; }

        public IReadOnlyList<string> Dependencies { get; }
    }
}
