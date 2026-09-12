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

        /// <summary>
        /// Optional backend field-type override. Auto infers the type from C#.
        /// </summary>
        public PlayServSchemaFieldType Type { get; set; } = PlayServSchemaFieldType.Auto;

        /// <summary>Stable field identity used by code-first schema pushes.</summary>
        public string CodeKey { get; set; } = string.Empty;

        public bool Primary { get; set; }
        public bool Unique { get; set; }
        public bool Indexed { get; set; }
        public string Default { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;
        public PlayServFieldCardinality Cardinality { get; set; } =
            PlayServFieldCardinality.Auto;
        public bool Ordered { get; set; }
    }

    public enum PlayServRequiredMode
    {
        Auto,
        Required,
        Optional
    }

    public enum PlayServFieldCardinality
    {
        Auto,
        One,
        Many
    }

    public enum PlayServSchemaFieldType
    {
        Auto,
        Text,
        LongText,
        Integer,
        Decimal,
        Number,
        Boolean,
        DateTime,
        Date,
        Email,
        Url,
        Uuid,
        Json,
        Enum,
        Reference,
        Relation,
        Inclusion
    }
}
