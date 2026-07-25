using System;

namespace Playserv.Schema
{
    /// <summary>
    /// Records a previous contract or member name for rename-aware migrations.
    /// </summary>
    [AttributeUsage(
        AttributeTargets.Class |
        AttributeTargets.Struct |
        AttributeTargets.Enum |
        AttributeTargets.Field |
        AttributeTargets.Property,
        AllowMultiple = true,
        Inherited = false)]
    public sealed class PlayServFormerlyNamedAttribute : Attribute
    {
        public PlayServFormerlyNamedAttribute(string name)
        {
            Name = string.IsNullOrWhiteSpace(name)
                ? throw new ArgumentException("Previous name is required.", nameof(name))
                : name.Trim();
        }

        public string Name { get; }
    }
}
