using System;

namespace Playserv.Schema
{
    /// <summary>
    /// Excludes a field or property from its containing PlayServ schema.
    /// </summary>
    [AttributeUsage(
        AttributeTargets.Field | AttributeTargets.Property,
        AllowMultiple = false,
        Inherited = false)]
    public sealed class PlayServIgnoreAttribute : Attribute
    {
    }
}
