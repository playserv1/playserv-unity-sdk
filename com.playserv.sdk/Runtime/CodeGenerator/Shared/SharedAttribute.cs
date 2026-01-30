// Shared.SDK.Annotations/SharedAttribute.cs
#nullable enable
using System;

namespace Playserv.Shared
{
    [AttributeUsage(AttributeTargets.Property| AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class SharedAttribute : Attribute
    {
        public SharedAttribute(Type rootType, string key)
        {
            RootType = rootType ?? throw new ArgumentNullException(nameof(rootType));
            Key = key ?? throw new ArgumentNullException(nameof(key));
        }

        public SharedAttribute(string key)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
        }

        public string Key { get; }

        public Type? RootType { get; set; }
        public string Id { get; set; } = "id";
        public string? Selection { get; set; }
        public string? Where { get; set; }
        public bool TwoWay { get; set; } = false;
        public int PatchDebounceMs { get; set; } = 80;
        public string? GeneratedName { get; set; }
    }
}
#nullable restore