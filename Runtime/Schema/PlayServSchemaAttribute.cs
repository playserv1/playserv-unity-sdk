using System;

namespace Playserv.Schema
{
    /// <summary>
    /// Marks a C# type as a source contract for the external PlayServ Schema Tool.
    /// </summary>
    [AttributeUsage(
        AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum,
        AllowMultiple = false,
        Inherited = false)]
    public sealed class PlayServSchemaAttribute : Attribute
    {
        public PlayServSchemaAttribute(string id = null)
        {
            Id = id ?? string.Empty;
        }

        /// <summary>
        /// Stable contract ID. It should not change when the C# type is renamed.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// Declares which source owns changes to this contract.
        /// </summary>
        public PlayServSchemaAuthority Authority { get; set; } =
            PlayServSchemaAuthority.Contract;

        /// <summary>
        /// Optional contract version supplied by the project.
        /// </summary>
        public string Version { get; set; } = string.Empty;

        /// <summary>Optional backend-facing display description.</summary>
        public string Description { get; set; } = string.Empty;

        public PlayServSchemaKind Kind { get; set; } = PlayServSchemaKind.Auto;

        public bool Singleton { get; set; }

        public string DisplayField { get; set; } = string.Empty;

        public PlayServSchemaOwner OwnedBy { get; set; } = PlayServSchemaOwner.None;

        public PlayServSchemaReadPolicy Read { get; set; } = PlayServSchemaReadPolicy.Default;

        public PlayServPlayerDeletePolicy OnPlayerDelete { get; set; } =
            PlayServPlayerDeletePolicy.None;

        public bool AllowRawFields { get; set; }

        public PlayServSchemaAccess ClientRead { get; set; } = PlayServSchemaAccess.Default;
        public PlayServSchemaAccess ClientWrite { get; set; } = PlayServSchemaAccess.Default;
        public PlayServSchemaAccess ServerRead { get; set; } = PlayServSchemaAccess.Default;
        public PlayServSchemaAccess ServerWrite { get; set; } = PlayServSchemaAccess.Default;
        public PlayServSchemaAccess BackendRead { get; set; } = PlayServSchemaAccess.Default;
        public PlayServSchemaAccess BackendWrite { get; set; } = PlayServSchemaAccess.Default;
    }

    public enum PlayServSchemaAuthority
    {
        Contract,
        Client,
        Server,
        Admin
    }

    public enum PlayServSchemaKind
    {
        Auto,
        Entity,
        Part,
        Enum
    }

    public enum PlayServSchemaOwner
    {
        None,
        Player
    }

    public enum PlayServSchemaReadPolicy
    {
        Default,
        Owner,
        Public
    }

    public enum PlayServPlayerDeletePolicy
    {
        None,
        CascadeDelete,
        Restrict,
        Anonymise
    }

    public enum PlayServSchemaAccess
    {
        Default,
        Allow,
        Deny
    }
}
