// Shared.SDK.Annotations/SharedAttribute.cs
#nullable enable
using System;

namespace Playserv.Shared
{
    /// <summary>
    /// Declares shared DTO binding metadata used by PlayServ code generator.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property| AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class SharedAttribute : Attribute
    {
        /// <summary>
        /// Creates shared binding metadata with explicit root model type.
        /// </summary>
        /// <param name="rootType">Root model type for selection.</param>
        /// <param name="key">Binding key used in generated query.</param>
        public SharedAttribute(Type rootType, string key)
        {
            RootType = rootType ?? throw new ArgumentNullException(nameof(rootType));
            Key = key ?? throw new ArgumentNullException(nameof(key));
        }

        /// <summary>
        /// Creates shared binding metadata with key only.
        /// </summary>
        /// <param name="key">Binding key used in generated query.</param>
        public SharedAttribute(string key)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
        }

        /// <summary>
        /// Binding key used by generator.
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// Optional root model type.
        /// </summary>
        public Type? RootType { get; set; }

        /// <summary>
        /// Id field name used in query generation.
        /// </summary>
        public string Id { get; set; } = "id";

        /// <summary>
        /// Optional selection string for generated DTO fields.
        /// </summary>
        public string? Selection { get; set; }

        /// <summary>
        /// Optional where clause metadata.
        /// </summary>
        public string? Where { get; set; }

        /// <summary>
        /// Enables two-way binding generation when true.
        /// </summary>
        public bool TwoWay { get; set; } = false;

        /// <summary>
        /// Debounce interval for patch generation in milliseconds.
        /// </summary>
        public int PatchDebounceMs { get; set; } = 80;

        /// <summary>
        /// Optional explicit generated DTO type name.
        /// </summary>
        public string? GeneratedName { get; set; }
    }
}
#nullable restore
