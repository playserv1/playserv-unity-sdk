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
    }

    public enum PlayServSchemaAuthority
    {
        Contract,
        Client,
        Server,
        Admin
    }
}
