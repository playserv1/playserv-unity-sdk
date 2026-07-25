using System;

namespace Playserv.Schema
{
    /// <summary>
    /// Overrides generated schema metadata for a public field or property.
    /// </summary>
    [AttributeUsage(
        AttributeTargets.Field | AttributeTargets.Property,
        AllowMultiple = false,
        Inherited = false)]
    public sealed class PlayServFieldAttribute : Attribute
    {
        public PlayServFieldAttribute(string name = null)
        {
            Name = name ?? string.Empty;
        }

        public string Name { get; }

        public PlayServRequiredMode Required { get; set; } =
            PlayServRequiredMode.Auto;
    }

    public enum PlayServRequiredMode
    {
        Auto,
        Required,
        Optional
    }
}
